using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Realtime;

namespace Office.Api.Channels.WhatsApp;

/// <summary>Провайдери воқеии WhatsApp Cloud API (Meta Graph API).</summary>
public class WhatsAppProvider(
    HttpClient httpClient,
    IChannelCredentialsProtector protector,
    IConfiguration configuration,
    AppDbContext db,
    INotificationService notificationService,
    ILogger<WhatsAppProvider> logger) : IChannelProvider
{
    private const string GraphApiVersion = "v23.0";
    private const string GraphApiBaseUrl = "https://graph.facebook.com";
    private const int WindowClosedErrorCode = 131047;
    private const int TokenExpiredErrorCode = 190;
    private const int RateLimitErrorCode = 4;
    private const int BusinessRateLimitErrorCode = 80007;

    public bool VerifyWebhookToken(string verifyToken)
    {
        var expected = configuration["Webhooks:VerifyToken"];
        return !string.IsNullOrEmpty(expected) && verifyToken == expected;
    }

    public string? ExtractChannelExternalId(JsonElement payload) => WhatsAppPayloadParser.ExtractChannelExternalId(payload);

    public Task<IReadOnlyList<ParsedWebhookMessage>> ParseWebhookAsync(Channel channel, JsonElement payload, CancellationToken ct) =>
        Task.FromResult(WhatsAppPayloadParser.ParseMessages(payload));

    public Task<IReadOnlyList<ParsedStatusUpdate>> ParseStatusUpdatesAsync(Channel channel, JsonElement payload, CancellationToken ct) =>
        Task.FromResult(WhatsAppPayloadParser.ParseStatusUpdates(payload));

    public async Task SendMessageAsync(Channel channel, string conversationExternalId, string body, CancellationToken ct)
    {
        var credentials = GetCredentials(channel);
        var payload = new
        {
            messaging_product = "whatsapp",
            recipient_type = "individual",
            to = conversationExternalId,
            type = "text",
            text = new { body },
        };

        await PostToGraphApiAsync(credentials, $"{credentials.PhoneNumberId}/messages", payload, ct);
    }

    public async Task SendTemplateAsync(
        Channel channel, string conversationExternalId, string templateName, string languageCode,
        IReadOnlyList<string> parameters, CancellationToken ct)
    {
        var credentials = GetCredentials(channel);
        var payload = new
        {
            messaging_product = "whatsapp",
            recipient_type = "individual",
            to = conversationExternalId,
            type = "template",
            template = new
            {
                name = templateName,
                language = new { code = languageCode },
                components = parameters.Count == 0
                    ? []
                    : new object[]
                    {
                        new { type = "body", parameters = parameters.Select(p => new { type = "text", text = p }).ToArray() },
                    },
            },
        };

        await PostToGraphApiAsync(credentials, $"{credentials.PhoneNumberId}/messages", payload, ct);
    }

    public async Task MarkAsReadAsync(Channel channel, string messageExternalId, CancellationToken ct)
    {
        var credentials = GetCredentials(channel);
        var payload = new { messaging_product = "whatsapp", status = "read", message_id = messageExternalId };

        await PostToGraphApiAsync(credentials, $"{credentials.PhoneNumberId}/messages", payload, ct);
    }

    public async Task<Stream> DownloadMediaAsync(Channel channel, string mediaExternalId, CancellationToken ct)
    {
        var credentials = GetCredentials(channel);

        using var metaRequest = new HttpRequestMessage(HttpMethod.Get, $"{GraphApiBaseUrl}/{GraphApiVersion}/{mediaExternalId}");
        metaRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        var metaResponse = await httpClient.SendAsync(metaRequest, ct);
        metaResponse.EnsureSuccessStatusCode();

        using var metaDoc = JsonDocument.Parse(await metaResponse.Content.ReadAsStreamAsync(ct));
        var mediaUrl = metaDoc.RootElement.GetProperty("url").GetString()!;

        using var downloadRequest = new HttpRequestMessage(HttpMethod.Get, mediaUrl);
        downloadRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        var downloadResponse = await httpClient.SendAsync(downloadRequest, ct);
        downloadResponse.EnsureSuccessStatusCode();

        var buffer = new MemoryStream();
        await downloadResponse.Content.CopyToAsync(buffer, ct);
        buffer.Position = 0;
        return buffer;
    }

    public async Task<IReadOnlyList<WhatsAppTemplateInfo>> GetApprovedTemplatesAsync(Channel channel, CancellationToken ct)
    {
        var credentials = GetCredentials(channel);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{GraphApiBaseUrl}/{GraphApiVersion}/{credentials.WabaId}/message_templates?fields=name,language,status,components");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);

        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        var result = new List<WhatsAppTemplateInfo>();

        if (!doc.RootElement.TryGetProperty("data", out var dataEl))
            return result;

        foreach (var item in dataEl.EnumerateArray())
        {
            var name = item.GetProperty("name").GetString()!;
            var language = item.GetProperty("language").GetString()!;
            var status = item.GetProperty("status").GetString()!;
            string? bodyText = null;

            if (item.TryGetProperty("components", out var componentsEl))
            {
                foreach (var component in componentsEl.EnumerateArray())
                {
                    if (component.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "BODY" &&
                        component.TryGetProperty("text", out var textEl))
                    {
                        bodyText = textEl.GetString();
                        break;
                    }
                }
            }

            result.Add(new WhatsAppTemplateInfo(name, language, status, bodyText));
        }

        return result;
    }

    private WhatsAppCredentials GetCredentials(Channel channel)
    {
        if (string.IsNullOrEmpty(channel.CredentialsEncrypted))
            throw new InvalidOperationException("Канал credentials надорад.");

        return WhatsAppCredentials.Parse(protector.Unprotect(channel.CredentialsEncrypted));
    }

    private async Task PostToGraphApiAsync(WhatsAppCredentials credentials, string path, object payload, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{GraphApiBaseUrl}/{GraphApiVersion}/{path}")
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);

        var response = await httpClient.SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
            return;

        var responseBody = await response.Content.ReadAsStringAsync(ct);
        var errorCode = TryGetErrorCode(responseBody);

        if (errorCode == WindowClosedErrorCode)
            throw new WhatsAppWindowClosedException("Тирезаи 24-соата вайрон шудааст — танҳо шаблон фиристода мешавад.");

        if (errorCode == TokenExpiredErrorCode)
            await NotifyOwnersAsync("WhatsApp: токени дастрасӣ эътибор надорад ё тамом шудааст. Каналро санҷед.", ct);
        else if (errorCode is RateLimitErrorCode or BusinessRateLimitErrorCode)
            await NotifyOwnersAsync("WhatsApp: маҳдудияти дархост (rate limit) расид. Каналро санҷед.", ct);

        logger.LogError("WhatsApp Graph API хатогӣ: {StatusCode} {Body}", (int)response.StatusCode, responseBody);
        throw new InvalidOperationException($"WhatsApp Graph API хатогӣ: {responseBody}");
    }

    private async Task NotifyOwnersAsync(string message, CancellationToken ct)
    {
        var ownerIds = await db.Users
            .Where(u => u.IsActive && u.UserRoles.Any(ur => ur.Role.Key == RoleKeys.Owner))
            .Select(u => u.Id)
            .ToListAsync(ct);

        foreach (var ownerId in ownerIds)
            await notificationService.PushAsync(ownerId, "whatsapp_error", new { message }, ct);
    }

    private static int? TryGetErrorCode(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            return doc.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("code", out var codeEl)
                ? codeEl.GetInt32()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
