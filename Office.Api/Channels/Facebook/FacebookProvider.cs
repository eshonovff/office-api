using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Channels.WhatsApp;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Realtime;

namespace Office.Api.Channels.Facebook;

/// <summary>Провайдери воқеии Facebook Messenger (Meta Graph API, Page-based).</summary>
public class FacebookProvider(
    HttpClient httpClient,
    IChannelCredentialsProtector protector,
    IConfiguration configuration,
    AppDbContext db,
    INotificationService notificationService,
    ILogger<FacebookProvider> logger) : IChannelProvider
{
    private const string GraphApiVersion = "v23.0";
    private const string GraphApiBaseUrl = "https://graph.facebook.com";
    private const int TokenExpiredErrorCode = 190;
    private const int RateLimitErrorCode = 4;
    private const int UserRateLimitErrorCode = 17;
    private const int PageRateLimitErrorCode = 32;
    private const int SendApiRateLimitErrorCode = 80004;

    public bool VerifyWebhookToken(string verifyToken)
    {
        var expected = configuration["Webhooks:VerifyToken"];
        return !string.IsNullOrEmpty(expected) && verifyToken == expected;
    }

    public string? ExtractChannelExternalId(JsonElement payload) => FacebookPayloadParser.ExtractChannelExternalId(payload);

    public Task<IReadOnlyList<ParsedWebhookMessage>> ParseWebhookAsync(Channel channel, JsonElement payload, CancellationToken ct)
    {
        var messages = FacebookPayloadParser.ParseMessages(payload);

        // Парсер pure аст (бе logger) — ин ҷо, дар қабати провайдер, натиҷаро месанҷем: агар
        // навъе дастгирӣ нашуда бошад, паём боз ҳам сабт мешавад (хомӯшона гум намешавад),
        // вале ҳамзамон ин ҷо ҳам log мешавад — то бидонем, кадом навъи нав аз Meta омад.
        foreach (var message in messages)
        {
            if (message.Body?.StartsWith(FacebookPayloadParser.UnsupportedTypeBodyPrefix, StringComparison.Ordinal) == true)
                logger.LogWarning("Facebook: паёми навъи дастгирӣнашуда сабт шуд: {Body}", message.Body);
        }

        return Task.FromResult(messages);
    }

    public Task<IReadOnlyList<ParsedStatusUpdate>> ParseStatusUpdatesAsync(Channel channel, JsonElement payload, CancellationToken ct) =>
        Task.FromResult(FacebookPayloadParser.ParseStatusUpdates(payload));

    public async Task<string?> SendMessageAsync(Channel channel, string conversationExternalId, string body, string? messageTag, CancellationToken ct)
    {
        var credentials = GetCredentials(channel);
        var payload = BuildMessagePayload(conversationExternalId, new { text = body }, messageTag);

        var responseBody = await PostToGraphApiAsync(credentials, "messages", payload, ct);
        return FacebookPayloadParser.ExtractSentMessageId(responseBody);
    }

    public Task<string?> SendTemplateAsync(
        Channel channel, string conversationExternalId, string templateName, string languageCode,
        IReadOnlyList<string> parameters, CancellationToken ct) =>
        throw new NotSupportedException(
            "Facebook шаблон надорад — берун аз тиреза SendMessageAsync-и бо messageTag (масалан HUMAN_AGENT) истифода баред.");

    public Task MarkAsReadAsync(Channel channel, string messageExternalId, CancellationToken ct) =>
        // Facebook-и "mark_seen" ба recipient (PSID) ниёз дорад, на message_id — ин интерфейс
        // WhatsApp-хос аст (яке аз ду параметр ба Facebook рост намеояд). Бе caller (IChannelProvider.
        // MarkAsReadAsync дар ягон ҷои коди барнома то ҳол даъват намешавад — грепи пурра тасдиқ кард),
        // амалисозии нодуруст бо messageExternalId-ро ба ҷои recipient гузоштан хатарнок аст —
        // ошкоро рад мекунам, то агар касе баъдан ин методро вобаста кунад, хато фавран намоён шавад.
        throw new NotSupportedException(
            "Facebook mark_seen ба recipient (PSID) ниёз дорад, на message_id — ин интерфейс инро надорад.");

    public async Task<DownloadedMedia> DownloadMediaAsync(Channel channel, string mediaExternalId, CancellationToken ct)
    {
        // Бар хилофи WhatsApp: mediaExternalId дар ин ҷо худи URL-и CDN (бо мӯҳлат) аст, на id-е
        // ки бояд пеш ҳал шавад — FacebookPayloadParser онро мустақим аз attachment.payload.url
        // мегирад. URL худаш аллакай ИМЗОШУДА аст (query string) — Bearer-и иловагӣ лозим нест
        // ва CDN-и Meta ба он ғайричашмдошта ҷавоб медиҳад (200 бо саҳифаи HTML-и хатогӣ, на 401) —
        // бе санҷиши Content-Type (поён) ин ҳамчун "муваффақ" сабт мешуд.
        using var request = new HttpRequestMessage(HttpMethod.Get, mediaExternalId);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var buffer = new MemoryStream();
        await response.Content.CopyToAsync(buffer, ct);
        buffer.Position = 0;
        return new DownloadedMedia(buffer, response.Content.Headers.ContentType?.MediaType);
    }

    public async Task<string> UploadMediaAsync(Channel channel, Stream content, string mimeType, string fileName, CancellationToken ct)
    {
        var credentials = GetCredentials(channel);

        using var form = new MultipartFormDataContent();
        // is_reusable=true: attachment_id-ро метавон дар якчанд паём истифода бурд — ба мо лозим
        // нест (як фиристодан як мессиҷ), вале акнун сохтани дубора ба ҳар message заруратро бартараф мекунад.
        form.Add(new StringContent("""{"attachment":{"type":"file","payload":{"is_reusable":true}}}"""), "message");
        using var streamContent = new StreamContent(content);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        form.Add(streamContent, "filedata", fileName);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{GraphApiBaseUrl}/{GraphApiVersion}/me/message_attachments")
        {
            Content = form,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.PageAccessToken);

        var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Facebook message_attachments хатогӣ: {StatusCode} {Body}", (int)response.StatusCode, responseBody);
            throw new InvalidOperationException($"Facebook message_attachments хатогӣ: {responseBody}");
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        return doc.RootElement.GetProperty("attachment_id").GetString()!;
    }

    public async Task<string?> SendMediaMessageAsync(
        Channel channel, string conversationExternalId, string mediaExternalId, MessageType type,
        string? caption, bool isVoiceNote, string? messageTag, CancellationToken ct)
    {
        var credentials = GetCredentials(channel);
        var attachmentType = ToFacebookAttachmentType(type);

        var payload = BuildMessagePayload(
            conversationExternalId,
            new { attachment = new { type = attachmentType, payload = new { attachment_id = mediaExternalId } } },
            messageTag);

        var responseBody = await PostToGraphApiAsync(credentials, "messages", payload, ct);
        var wamid = FacebookPayloadParser.ExtractSentMessageId(responseBody);

        // Send API-и Facebook як message object (ё матн, ё attachment) мегирад — на ҳарду якҷоя.
        // Caption бо дархости дуюм меравад; wamid-и бармегашта ҳамон медиа мемонад (алгуи WhatsApp,
        // ки id-и матни асосиро бармегардонад).
        if (caption is { Length: > 0 })
        {
            var captionPayload = BuildMessagePayload(conversationExternalId, new { text = caption }, messageTag);
            await PostToGraphApiAsync(credentials, "messages", captionPayload, ct);
        }

        return wamid;
    }

    public Task<IReadOnlyList<WhatsAppTemplateInfo>> GetApprovedTemplatesAsync(Channel channel, CancellationToken ct) =>
        throw new NotSupportedException("Facebook шаблон надорад.");

    /// <summary>
    /// НАЗАРАСОН: ин User Profile API дар солҳои охир аз ҷониби Meta маҳдуд шудааст (баъзе
    /// пермишни иловагӣ метавонад лозим ояд) — санҷиши зинда лозим аст. Хатогӣ ба ин ҷо
    /// (масалан 403/permission) ҳеҷ гоҳ намепартояд — танҳо log ва ContactProfile.Empty:
    /// коркарди webhook (сабти худи паём) набояд аз номи мижоз вобаста бошад.
    /// </summary>
    public async Task<ContactProfile> GetContactProfileAsync(Channel channel, string contactExternalId, CancellationToken ct)
    {
        var credentials = GetCredentials(channel);
        var url = $"{GraphApiBaseUrl}/{GraphApiVersion}/{contactExternalId}?fields=first_name,last_name,profile_pic";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.PageAccessToken);
        var response = await httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning(
                "Facebook контакт {ContactExternalId} гирифта нашуд: {StatusCode} {Body}", contactExternalId, (int)response.StatusCode, body);
            return ContactProfile.Empty;
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        var firstName = doc.RootElement.TryGetProperty("first_name", out var firstEl) ? firstEl.GetString() : null;
        var lastName = doc.RootElement.TryGetProperty("last_name", out var lastEl) ? lastEl.GetString() : null;
        var name = string.Join(' ', new[] { firstName, lastName }.Where(n => !string.IsNullOrEmpty(n)));
        var avatarUrl = doc.RootElement.TryGetProperty("profile_pic", out var picEl) ? picEl.GetString() : null;

        return new ContactProfile(string.IsNullOrEmpty(name) ? null : name, avatarUrl);
    }

    private static object BuildMessagePayload(string conversationExternalId, object message, string? messageTag) =>
        messageTag is null
            ? new { recipient = new { id = conversationExternalId }, messaging_type = "RESPONSE", message }
            : new { recipient = new { id = conversationExternalId }, messaging_type = "MESSAGE_TAG", tag = messageTag, message };

    private static string ToFacebookAttachmentType(MessageType type) => type switch
    {
        MessageType.Image => "image",
        MessageType.Video => "video",
        MessageType.Audio => "audio",
        MessageType.File => "file",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Ин навъи паём медиа надорад."),
    };

    private FacebookCredentials GetCredentials(Channel channel)
    {
        if (string.IsNullOrEmpty(channel.CredentialsEncrypted))
            throw new InvalidOperationException("Канал credentials надорад.");

        return FacebookCredentials.Parse(protector.Unprotect(channel.CredentialsEncrypted));
    }

    private async Task<string> PostToGraphApiAsync(FacebookCredentials credentials, string path, object payload, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{GraphApiBaseUrl}/{GraphApiVersion}/{credentials.PageId}/{path}")
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.PageAccessToken);

        var response = await httpClient.SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
            return await response.Content.ReadAsStringAsync(ct);

        var responseBody = await response.Content.ReadAsStringAsync(ct);
        var errorCode = TryGetErrorCode(responseBody);

        if (errorCode == TokenExpiredErrorCode)
            await NotifyOwnersAsync("Facebook: токени дастрасии Page эътибор надорад ё тамом шудааст. Каналро санҷед.", ct);
        else if (errorCode is RateLimitErrorCode or UserRateLimitErrorCode or PageRateLimitErrorCode or SendApiRateLimitErrorCode)
            await NotifyOwnersAsync("Facebook: маҳдудияти дархост (rate limit) расид. Каналро санҷед.", ct);

        logger.LogError("Facebook Graph API хатогӣ: {StatusCode} {Body}", (int)response.StatusCode, responseBody);
        throw new InvalidOperationException($"Facebook Graph API хатогӣ: {responseBody}");
    }

    private async Task NotifyOwnersAsync(string message, CancellationToken ct)
    {
        var ownerIds = await db.Users
            .Where(u => u.IsActive && u.UserRoles.Any(ur => ur.Role.Key == RoleKeys.Owner))
            .Select(u => u.Id)
            .ToListAsync(ct);

        foreach (var ownerId in ownerIds)
            await notificationService.PushAsync(ownerId, "facebook_error", new { message }, ct);
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
