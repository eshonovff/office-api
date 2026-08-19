using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Channels.WhatsApp;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Realtime;

namespace Office.Api.Channels.Instagram;

/// <summary>
/// Провайдери воқеии Instagram Messaging (Instagram API with Instagram Login) — на
/// graph.facebook.com, балки graph.instagram.com (ҳамон host, ки OAuth-и ин маҳсулот
/// истифода мебарад, ниг. InstagramOAuthConnector).
/// </summary>
public class InstagramProvider(
    HttpClient httpClient,
    IChannelCredentialsProtector protector,
    IConfiguration configuration,
    AppDbContext db,
    INotificationService notificationService,
    ILogger<InstagramProvider> logger) : IChannelProvider
{
    private const string GraphApiVersion = "v23.0";
    private const string GraphApiBaseUrl = "https://graph.instagram.com";
    private const int TokenExpiredErrorCode = 190;
    private const int RateLimitErrorCode = 4;
    private const int UserRateLimitErrorCode = 17;
    private const int SendApiRateLimitErrorCode = 80004;

    public bool VerifyWebhookToken(string verifyToken)
    {
        var expected = configuration["Webhooks:VerifyToken"];
        return !string.IsNullOrEmpty(expected) && verifyToken == expected;
    }

    public string? ExtractChannelExternalId(JsonElement payload) => InstagramPayloadParser.ExtractChannelExternalId(payload);

    public Task<IReadOnlyList<ParsedWebhookMessage>> ParseWebhookAsync(Channel channel, JsonElement payload, CancellationToken ct) =>
        Task.FromResult(InstagramPayloadParser.ParseMessages(payload));

    public Task<IReadOnlyList<ParsedStatusUpdate>> ParseStatusUpdatesAsync(Channel channel, JsonElement payload, CancellationToken ct) =>
        Task.FromResult(InstagramPayloadParser.ParseStatusUpdates(payload));

    public async Task<string?> SendMessageAsync(Channel channel, string conversationExternalId, string body, string? messageTag, CancellationToken ct)
    {
        var credentials = GetCredentials(channel);
        var payload = BuildMessagePayload(conversationExternalId, new { text = body }, messageTag);

        var responseBody = await PostToGraphApiAsync(credentials, "messages", payload, ct);
        return InstagramPayloadParser.ExtractSentMessageId(responseBody);
    }

    public Task<string?> SendTemplateAsync(
        Channel channel, string conversationExternalId, string templateName, string languageCode,
        IReadOnlyList<string> parameters, CancellationToken ct) =>
        throw new NotSupportedException(
            "Instagram шаблон надорад — берун аз тиреза SendMessageAsync-и бо messageTag (масалан HUMAN_AGENT) истифода баред.");

    public Task MarkAsReadAsync(Channel channel, string messageExternalId, CancellationToken ct) =>
        // Ҳамон мушкили Facebook: mark_seen ба recipient (PSID) ниёз дорад, на message_id.
        throw new NotSupportedException(
            "Instagram mark_seen ба recipient (PSID) ниёз дорад, на message_id — ин интерфейс инро надорад.");

    public async Task<Stream> DownloadMediaAsync(Channel channel, string mediaExternalId, CancellationToken ct)
    {
        var credentials = GetCredentials(channel);

        // Ҳамон алгуи Facebook: mediaExternalId худи URL-и CDN аст, на id-е ки бояд ҳал шавад.
        using var request = new HttpRequestMessage(HttpMethod.Get, mediaExternalId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var buffer = new MemoryStream();
        await response.Content.CopyToAsync(buffer, ct);
        buffer.Position = 0;
        return buffer;
    }

    /// <summary>
    /// НАЗАРАСОН: message_attachments-и Facebook барои Instagram санҷиши зинда нашудааст —
    /// ин ҷо ҳамон endpoint/шакл фарз карда шудааст (graph.instagram.com-и ҳамон host).
    /// Агар Meta барои Instagram шакли дигар талаб кунад, ин метод бояд аввалин бошад, ки санҷида мешавад.
    /// </summary>
    public async Task<string> UploadMediaAsync(Channel channel, Stream content, string mimeType, string fileName, CancellationToken ct)
    {
        var credentials = GetCredentials(channel);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("""{"attachment":{"type":"file","payload":{"is_reusable":true}}}"""), "message");
        using var streamContent = new StreamContent(content);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        form.Add(streamContent, "filedata", fileName);

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{GraphApiBaseUrl}/{GraphApiVersion}/{credentials.InstagramAccountId}/message_attachments")
        {
            Content = form,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);

        var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Instagram message_attachments хатогӣ: {StatusCode} {Body}", (int)response.StatusCode, responseBody);
            throw new InvalidOperationException($"Instagram message_attachments хатогӣ: {responseBody}");
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        return doc.RootElement.GetProperty("attachment_id").GetString()!;
    }

    public async Task<string?> SendMediaMessageAsync(
        Channel channel, string conversationExternalId, string mediaExternalId, MessageType type,
        string? caption, bool isVoiceNote, string? messageTag, CancellationToken ct)
    {
        var credentials = GetCredentials(channel);
        var attachmentType = ToInstagramAttachmentType(type);

        var payload = BuildMessagePayload(
            conversationExternalId,
            new { attachment = new { type = attachmentType, payload = new { attachment_id = mediaExternalId } } },
            messageTag);

        var responseBody = await PostToGraphApiAsync(credentials, "messages", payload, ct);
        var messageId = InstagramPayloadParser.ExtractSentMessageId(responseBody);

        // Send API як message object (матн ё attachment) мегирад — на ҳарду якҷоя, ҳамон Facebook.
        if (caption is { Length: > 0 })
        {
            var captionPayload = BuildMessagePayload(conversationExternalId, new { text = caption }, messageTag);
            await PostToGraphApiAsync(credentials, "messages", captionPayload, ct);
        }

        return messageId;
    }

    public Task<IReadOnlyList<WhatsAppTemplateInfo>> GetApprovedTemplatesAsync(Channel channel, CancellationToken ct) =>
        throw new NotSupportedException("Instagram шаблон надорад.");

    private static object BuildMessagePayload(string conversationExternalId, object message, string? messageTag) =>
        messageTag is null
            ? new { recipient = new { id = conversationExternalId }, messaging_type = "RESPONSE", message }
            : new { recipient = new { id = conversationExternalId }, messaging_type = "MESSAGE_TAG", tag = messageTag, message };

    private static string ToInstagramAttachmentType(MessageType type) => type switch
    {
        MessageType.Image => "image",
        MessageType.Video => "video",
        MessageType.Audio => "audio",
        MessageType.File => "file",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Ин навъи паём медиа надорад."),
    };

    private InstagramCredentials GetCredentials(Channel channel)
    {
        if (string.IsNullOrEmpty(channel.CredentialsEncrypted))
            throw new InvalidOperationException("Канал credentials надорад.");

        return InstagramCredentials.Parse(protector.Unprotect(channel.CredentialsEncrypted));
    }

    private async Task<string> PostToGraphApiAsync(InstagramCredentials credentials, string path, object payload, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{GraphApiBaseUrl}/{GraphApiVersion}/{credentials.InstagramAccountId}/{path}")
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);

        var response = await httpClient.SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
            return await response.Content.ReadAsStringAsync(ct);

        var responseBody = await response.Content.ReadAsStringAsync(ct);
        var errorCode = TryGetErrorCode(responseBody);

        if (errorCode == TokenExpiredErrorCode)
            await NotifyOwnersAsync("Instagram: токени дастрасӣ эътибор надорад ё тамом шудааст. Каналро санҷед.", ct);
        else if (errorCode is RateLimitErrorCode or UserRateLimitErrorCode or SendApiRateLimitErrorCode)
            await NotifyOwnersAsync("Instagram: маҳдудияти дархост (rate limit) расид. Каналро санҷед.", ct);

        logger.LogError("Instagram Graph API хатогӣ: {StatusCode} {Body}", (int)response.StatusCode, responseBody);
        throw new InvalidOperationException($"Instagram Graph API хатогӣ: {responseBody}");
    }

    private async Task NotifyOwnersAsync(string message, CancellationToken ct)
    {
        var ownerIds = await db.Users
            .Where(u => u.IsActive && u.UserRoles.Any(ur => ur.Role.Key == RoleKeys.Owner))
            .Select(u => u.Id)
            .ToListAsync(ct);

        foreach (var ownerId in ownerIds)
            await notificationService.PushAsync(ownerId, "instagram_error", new { message }, ct);
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
