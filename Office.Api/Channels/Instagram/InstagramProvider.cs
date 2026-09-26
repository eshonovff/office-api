using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Office.Api.Auth;
using Office.Api.Channels;
using Office.Api.Channels.WhatsApp;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Media;
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
    IMemoryCache cache,
    InstagramFollowCheckRateLimiter followCheckRateLimiter,
    ILogger<InstagramProvider> logger) : IChannelProvider
{
    private const string GraphApiVersion = "v23.0";
    private const string GraphApiBaseUrl = "https://graph.instagram.com";
    private const int TokenExpiredErrorCode = 190;
    private const int RateLimitErrorCode = 4;
    private const int UserRateLimitErrorCode = 17;
    private const int SendApiRateLimitErrorCode = 80004;

    /// <summary>80% аз 200/соат-и Meta (спецификатсияи Фазаи 11) — ниг. InstagramFollowCheckRateLimiter.</summary>
    private const int FollowCheckHourlyBudget = 160;

    public bool VerifyWebhookToken(string verifyToken)
    {
        var expected = configuration["Webhooks:VerifyToken"];
        return !string.IsNullOrEmpty(expected) && verifyToken == expected;
    }

    public string? ExtractChannelExternalId(JsonElement payload) => InstagramPayloadParser.ExtractChannelExternalId(payload);

    public Task<IReadOnlyList<ParsedWebhookMessage>> ParseWebhookAsync(Channel channel, JsonElement payload, CancellationToken ct)
    {
        var messages = InstagramPayloadParser.ParseMessages(payload);

        // Парсер pure аст (бе logger) — ин ҷо, дар қабати провайдер, натиҷаро месанҷем: агар
        // навъе дастгирӣ нашуда бошад, паём боз ҳам сабт мешавад (хомӯшона гум намешавад),
        // вале ҳамзамон ин ҷо ҳам log мешавад — то бидонем, кадом навъи нав аз Meta омад.
        foreach (var message in messages)
        {
            if (message.Body?.StartsWith(InstagramPayloadParser.UnsupportedTypeBodyPrefix, StringComparison.Ordinal) == true)
                logger.LogWarning("Instagram: паёми навъи дастгирӣнашуда сабт шуд: {Body}", message.Body);

            // МУВАҚҚАТӢ ТАШХИС (2026-09-17, санҷиши зиндаи флоу бо тугма): PostbackPayload
            // ҳеҷ токен надорад (танҳо {sessionId}:{nodeId}:{buttonIndex}-и худамон) — сабти
            // пурра бехатар аст. Мақсад: тасдиқ кардан, ки Meta воқеан "postback" мефиристад
            // (на танҳо "message"-и матнии title-и тугма), пас аз тасдиқи messaging_postbacks
            // дар App Dashboard.
            if (message.PostbackPayload is not null)
                logger.LogInformation(
                    "Instagram: postback гирифта шуд — Payload={Payload}, Body={Body}", message.PostbackPayload, message.Body);
        }

        // МУВАҚҚАТӢ ТАШХИС: ин payload-ҳо ҳеҷ токен/парол надоранд (url + title, ҳамин
        // тасдиқшуд), пас пурра log кардан бехатар аст — то бидонем, оё Meta майдони
        // preview/thumbnail низ мефиристад (ниг. ExtractExternalContentPayloadsForDiagnostics).
        foreach (var rawPayload in InstagramPayloadParser.ExtractExternalContentPayloadsForDiagnostics(payload))
            logger.LogInformation("Instagram: payload-и Reel/Post/Story (ташхис): {RawPayload}", rawPayload);

        return Task.FromResult(messages);
    }

    public Task<IReadOnlyList<ParsedStatusUpdate>> ParseStatusUpdatesAsync(Channel channel, JsonElement payload, CancellationToken ct) =>
        Task.FromResult(InstagramPayloadParser.ParseStatusUpdates(payload));

    /// <summary>
    /// Паёми муқаррарӣ (recipient.id, на comment_id — фарқ аз SendPrivateReplyAsync) бо тугмаҳои
    /// интерактивӣ — барои Flow Builder-и Фазаи 12. Тасдиқшуда бо ҳуҷҷати расмии Meta
    /// (2026-09-15): Instagram ин намуди паёмро дастгирӣ мекунад (генерик/button template),
    /// то 3 тугма, навъҳои "web_url" ва "postback" (тугмаи "next"-и flow ба "postback" бо
    /// payload-и худ табдил меёбад — ниг. Channels/Flows/FlowConfigs.cs.MessageButton).
    /// System.Text.Json.Nodes истифода мешавад (на анонимӣ тип) — то ҳар тугма танҳо
    /// майдонҳои марбут ба навъи худро дошта бошад (URL барои postback ё payload барои web_url
    /// набояд ҳатто ҳамчун null фиристода шавад).
    /// </summary>
    public async Task<string?> SendButtonMessageAsync(
        Channel channel, string conversationExternalId, string text, IReadOnlyList<InstagramSendButton> buttons,
        string? messageTag, CancellationToken ct)
    {
        var credentials = GetCredentials(channel);

        var buttonsArray = new System.Text.Json.Nodes.JsonArray();
        foreach (var button in buttons)
        {
            var buttonNode = new System.Text.Json.Nodes.JsonObject { ["type"] = button.Type, ["title"] = button.Title };
            if (button.Type == InstagramSendButton.TypeWebUrl)
                buttonNode["url"] = button.Url;
            else
                buttonNode["payload"] = button.Payload;
            buttonsArray.Add(buttonNode);
        }

        var attachment = new System.Text.Json.Nodes.JsonObject
        {
            ["type"] = "template",
            ["payload"] = new System.Text.Json.Nodes.JsonObject
            {
                ["template_type"] = "button",
                ["text"] = text,
                ["buttons"] = buttonsArray,
            },
        };

        var payload = BuildMessagePayload(conversationExternalId, new { attachment }, messageTag);
        var responseBody = await PostToGraphApiAsync(channel, credentials, "messages", payload, ct);
        return InstagramPayloadParser.ExtractSentMessageId(responseBody);
    }

    public async Task<string?> SendMessageAsync(Channel channel, string conversationExternalId, string body, string? messageTag, CancellationToken ct)
    {
        var credentials = GetCredentials(channel);
        var payload = BuildMessagePayload(conversationExternalId, new { text = body }, messageTag);

        var responseBody = await PostToGraphApiAsync(channel, credentials, "messages", payload, ct);
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

    public async Task<DownloadedMedia> DownloadMediaAsync(Channel channel, string mediaExternalId, CancellationToken ct)
    {
        // Ҳамон алгуи Facebook: mediaExternalId худи URL-и CDN-и имзошуда аст, на id-е ки бояд
        // ҳал шавад — Bearer-и иловагӣ лозим нест (ва CDN-и Meta ба он бо 200+HTML-и хатогӣ ҷавоб
        // медод, на 401 — бе санҷиши Content-Type поён ин ҳамчун "муваффақ" сабт мешуд).
        using var request = new HttpRequestMessage(HttpMethod.Get, mediaExternalId);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var buffer = new MemoryStream();
        await response.Content.CopyToAsync(buffer, ct);
        buffer.Position = 0;
        return new DownloadedMedia(buffer, response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// Uploads a file once (a reusable attachment_id for any later message). The attachment's
    /// type MUST be the one it is sent as: Instagram accepts an image uploaded as "file" (200) but
    /// then never sends it — POST /messages answers 500 "Service temporarily unavailable" (code 2,
    /// is_transient), every time, whatever the size. Checked live 2026-09-26 on the same account,
    /// same recipient: 106 KB and 1.2 MB images uploaded as "image" → sent (200); uploaded as
    /// "file" → 500. This — not App Review, as was thought in August — is why images from flows
    /// and broadcasts never arrived.
    /// </summary>
    public async Task<string> UploadMediaAsync(Channel channel, Stream content, string mimeType, string fileName, CancellationToken ct)
    {
        var credentials = GetCredentials(channel);

        var attachmentType = ToInstagramAttachmentType(MediaUploadValidator.Classify(ChannelType.Instagram, mimeType).Type);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(JsonSerializer.Serialize(
            new { attachment = new { type = attachmentType, payload = new { is_reusable = true } } })), "message");
        using var streamContent = new StreamContent(content);
        // Parse (на конструктор): mimeType метавонад параметр дошта бошад (масалан
        // "audio/webm;codecs=opus"-и MediaRecorder-и браузер) — конструктори MediaTypeHeaderValue
        // танҳо "type/subtype"-и холисро қабул мекунад ва бо параметр FormatException медиҳад.
        streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse(mimeType);
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
            logger.LogError(
                "Instagram message_attachments хатогӣ: {StatusCode} {Body} | rate-limit сарлавҳаҳо: {RateLimitHeaders}",
                (int)response.StatusCode, responseBody, MetaRateLimitHeaders.Describe(response.Headers) ?? "(нест)");
            throw new GraphApiException(MetaErrorTranslator.Translate(responseBody), responseBody);
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

        var responseBody = await PostToGraphApiAsync(channel, credentials, "messages", payload, ct);
        var messageId = InstagramPayloadParser.ExtractSentMessageId(responseBody);

        // Send API як message object (матн ё attachment) мегирад — на ҳарду якҷоя, ҳамон Facebook.
        if (caption is { Length: > 0 })
        {
            var captionPayload = BuildMessagePayload(conversationExternalId, new { text = caption }, messageTag);
            await PostToGraphApiAsync(channel, credentials, "messages", captionPayload, ct);
        }

        return messageId;
    }

    public Task<IReadOnlyList<WhatsAppTemplateInfo>> GetApprovedTemplatesAsync(Channel channel, CancellationToken ct) =>
        throw new NotSupportedException("Instagram шаблон надорад.");

    /// <summary>
    /// Ҷавоби ҷамъиятӣ ба коментарий: POST /{comment-id}/replies бо параметри "message" — ниёз
    /// ба scope-и instagram_business_manage_comments (аллакай дархост шудааст, ниг.
    /// InstagramOAuthConnector.Scopes). Тасдиқшуда бо ҳуҷҷати расмии Meta (Graph API — Comment
    /// Moderation, 2026-09-14): ҳеҷ маҳдудияти шумора надорад (бар хилофи private reply поён).
    /// </summary>
    /// <returns>The new reply's own comment id (to store it, and to recognise its webhook echo).</returns>
    public async Task<string?> ReplyToCommentAsync(Channel channel, string commentId, string message, CancellationToken ct)
    {
        var credentials = GetCredentials(channel);
        var body = await PostToAbsoluteGraphApiPathAsync(channel, credentials, $"{Uri.EscapeDataString(commentId)}/replies", new { message }, ct);
        return InstagramCommentParser.TryReadId(body);
    }

    /// <summary>Hide or show a comment on the account's own post (POST /{comment-id} hide=…).</summary>
    public async Task SetCommentHiddenAsync(Channel channel, string commentId, bool hidden, CancellationToken ct)
    {
        var credentials = GetCredentials(channel);
        await PostToAbsoluteGraphApiPathAsync(channel, credentials, Uri.EscapeDataString(commentId), new { hide = hidden }, ct);
    }

    /// <summary>Delete a comment on the account's own post (DELETE /{comment-id}) — irreversible.</summary>
    public async Task DeleteCommentAsync(Channel channel, string commentId, CancellationToken ct)
    {
        var credentials = GetCredentials(channel);
        await SendToGraphApiAsync(channel, credentials, HttpMethod.Delete, Uri.EscapeDataString(commentId), payload: null, ct);
    }

    /// <summary>
    /// A post's comments with their replies (GET /{media-id}/comments), newest pages first, up to
    /// <paramref name="max"/> top-level comments — for syncing a post the webhooks never covered.
    /// </summary>
    public async Task<IReadOnlyList<InstagramCommentItem>> GetCommentsAsync(Channel channel, string mediaId, int max, CancellationToken ct)
    {
        const string fields = "id,text,timestamp,username,from,hidden,replies{id,text,timestamp,username,from,hidden}";
        var credentials = GetCredentials(channel);
        var result = new List<InstagramCommentItem>();
        string? after = null;

        while (result.Count < max)
        {
            var path = $"{Uri.EscapeDataString(mediaId)}/comments?fields={Uri.EscapeDataString(fields)}&limit=50" +
                       (after is null ? "" : $"&after={Uri.EscapeDataString(after)}");
            var body = await SendToGraphApiAsync(channel, credentials, HttpMethod.Get, path, payload: null, ct);
            var (page, next) = InstagramCommentParser.ParsePage(body);
            result.AddRange(page);
            if (next is null || page.Count == 0)
                break;
            after = next;
        }

        return result;
    }

    /// <summary>
    /// Private reply дар DM: POST /{ig-id}/messages бо recipient.comment_id — ҳамон endpoint-и
    /// SendMessageAsync (recipient.id), вале бо comment_id ба ҷои user id. Тасдиқшуда бо
    /// ҳуҷҷати расмии Meta (Messenger Platform — Private Replies, 2026-09-14): (1) як бор барои
    /// як коментарий (кӯшиши дуюм хато медиҳад), (2) танҳо дар давоми 7 РӮЗ пас аз сохта шудани
    /// коментарий (барои пости оддӣ/reel — на Instagram Live, ки танҳо то анҷоми пахш кор мекунад).
    /// Ин ду маҳдудиятро ин методи содда санҷида наметавонад (Meta худаш хато медиҳад, агар
    /// вайрон шаванд) — CommentAutomationJob хатогиро сабт мекунад, дубора кӯшиш намекунад.
    /// Агар <paramref name="button"/> дода шавад, ба ҷои матни оддӣ button template (Messenger
    /// Platform) фиристода мешавад — тасдиқшуда бо ҳуҷҷати расмии Meta (2026-09-14): "text" то 640
    /// ҳарф, то 3 тугма (мо танҳо якто мефиристем). type="postback" низ дастгирӣ мешавад (2026-09-17,
    /// FlowEngine.ExecuteMessageNodeAsync) — ҳамон endpoint (POST /messages) барои recipient.id
    /// (SendButtonMessageAsync) ва recipient.comment_id рафтори якхела дорад, тасдиқи алоҳидаи Meta
    /// барои ин комбинатсия дар ҳуҷҷат ёфт нашуд, вале шакли payload комилан умумист. Дарозии
    /// сарлавҳаи тугма дар ҳуҷҷат возеҳ нест — 20 ҳарф (маҳдудияти маъмули Messenger Platform барои
    /// тугмаҳо) дар frontend (`maxLength`) татбиқ шудааст; агар нодуруст бошад, Meta худаш бо
    /// хатогии возеҳ рад мекунад (ниг. GraphApiException — сабт мешавад, дубора кӯшиш намешавад).
    /// </summary>
    public async Task<string?> SendPrivateReplyAsync(
        Channel channel, string commentId, string text, InstagramSendButton? button, CancellationToken ct)
    {
        var credentials = GetCredentials(channel);

        object message;
        if (button is null)
        {
            message = new { text };
        }
        else
        {
            var buttonNode = new System.Text.Json.Nodes.JsonObject { ["type"] = button.Type, ["title"] = button.Title };
            if (button.Type == InstagramSendButton.TypeWebUrl)
                buttonNode["url"] = button.Url;
            else
                buttonNode["payload"] = button.Payload;

            message = new
            {
                attachment = new System.Text.Json.Nodes.JsonObject
                {
                    ["type"] = "template",
                    ["payload"] = new System.Text.Json.Nodes.JsonObject
                    {
                        ["template_type"] = "button",
                        ["text"] = text,
                        ["buttons"] = new System.Text.Json.Nodes.JsonArray { buttonNode },
                    },
                },
            };
        }

        var payload = new { recipient = new { comment_id = commentId }, message };
        var responseBody = await PostToGraphApiAsync(channel, credentials, "messages", payload, ct);
        return InstagramPayloadParser.ExtractSentMessageId(responseBody);
    }

    /// <summary>
    /// Ҳамон Private Reply (ниг. SendPrivateReplyAsync боло), вале барои media (аз
    /// UploadMediaAsync-и дубора-истифодашаванда) ба ҷои матн. ДИҚҚАТ: бар хилофи
    /// SendPrivateReplyAsync (матн/тугма), ин шакли мушаххас — attachment тавассути
    /// recipient.comment_id — бо ҳуҷҷати расмии Meta ҷудогона тасдиқ НАШУДААСТ дар ин лоиҳа
    /// (танҳо шакли умумии Send API-и як object-и message фарз карда шудааст). Агар Meta ба ин
    /// комбинатсия хато диҳад, GraphApiException сабт мешавад — CommentAutomationJob-монанд
    /// дубора кӯшиш намекунад (маҳдудияти "як бор дар як коментарий" ҳамин тавр ҳам вайрон намешавад).
    /// </summary>
    public async Task<string?> SendPrivateReplyMediaAsync(
        Channel channel, string commentId, string mediaId, MessageType type, CancellationToken ct)
    {
        var credentials = GetCredentials(channel);
        var attachmentType = ToInstagramAttachmentType(type);

        var payload = new
        {
            recipient = new { comment_id = commentId },
            message = new { attachment = new { type = attachmentType, payload = new { attachment_id = mediaId } } },
        };

        var responseBody = await PostToGraphApiAsync(channel, credentials, "messages", payload, ct);
        return InstagramPayloadParser.ExtractSentMessageId(responseBody);
    }

    /// <summary>
    /// Фазаи 11: GET /{user-id}?fields=username,is_user_follow_business — тасдиқшуда дар
    /// истеҳсол (host: graph.instagram.com, на graph.facebook.com — токенҳои IGAB... дар
    /// graph.facebook.com хатои 190 медиҳанд). value.from.id-и webhook-и коментарий мустақиман
    /// ҳамчун user-id истифода мешавад — табдил лозим нест.
    ///
    /// ҲЕҶ ГОҲ истисно намепартояд — хатогии HTTP/JSON/токен ҳама ба Unknown мераванд (Warning,
    /// на Error — ин ҳолати муқаррарӣ аст: муштарӣ бе ҷавоб намонад, амали асосӣ (OnMatch) иҷро
    /// мешавад).
    ///
    /// Кэш (15 дақ) — ФАҚАТ барои Following/Unknown, НА NotFollowing: санҷиши зинда (2026-09-15)
    /// нишон дод, ки NotFollowing-и кэшшуда корбареро, ки ҳамон лаҳза воқеан обуна шуд, ҷазо
    /// медиҳад (то 15 дақ боз "обуна нест" мегирад — ҳарчанд обуна шудааст). Following/Unknown
    /// кэш кардан бехатар аст (ҳеҷ кас аз натиҷаи кӯҳна зарар намебинад). Ба ҷои кэши NotFollowing,
    /// буҷаи соатии <see cref="InstagramFollowCheckRateLimiter"/> (80% аз 200/соат-и Meta) ва
    /// идемпотентии як AutomationRun-и як comment_id (ниг. CommentAutomationProcessor) квотаро
    /// муҳофизат мекунанд.
    /// </summary>
    public async Task<FollowCheckResult> CheckFollowStatusAsync(Channel channel, string actorId, CancellationToken ct)
    {
        var cacheKey = $"ig-follow:{channel.Id}:{actorId}";
        if (cache.TryGetValue(cacheKey, out FollowCheckResult cached) && cached != FollowCheckResult.NotFollowing)
            return cached;

        if (!followCheckRateLimiter.TryConsume(FollowCheckHourlyBudget, DateTimeOffset.UtcNow))
        {
            logger.LogWarning(
                "Instagram follow-check: буҷаи соатӣ (80% аз 200/соат-и Meta) тамом шуд — Unknown бе дархост ({ActorId})", actorId);
            return FollowCheckResult.Unknown;
        }

        var result = await CheckFollowStatusUncachedAsync(channel, actorId, ct);
        if (result != FollowCheckResult.NotFollowing)
            cache.Set(cacheKey, result, TimeSpan.FromMinutes(15));
        return result;
    }

    private async Task<FollowCheckResult> CheckFollowStatusUncachedAsync(Channel channel, string actorId, CancellationToken ct)
    {
        InstagramCredentials credentials;
        try
        {
            credentials = GetCredentials(channel);
        }
        catch (InvalidOperationException)
        {
            return FollowCheckResult.Unknown;
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get, $"{GraphApiBaseUrl}/{GraphApiVersion}/{Uri.EscapeDataString(actorId)}?fields=username,is_user_follow_business");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);

            var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                var errorCode = TryGetErrorCode(body);
                if (errorCode == TokenExpiredErrorCode)
                {
                    channel.RequiresReconnect = true;
                    await db.SaveChangesAsync(ct);
                }

                logger.LogWarning(
                    "Instagram follow-check {ActorId} ноком шуд: {StatusCode} {Body}", actorId, (int)response.StatusCode, body);
                return FollowCheckResult.Unknown;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
            if (!doc.RootElement.TryGetProperty("is_user_follow_business", out var followEl))
                return FollowCheckResult.Unknown;

            return followEl.GetBoolean() ? FollowCheckResult.Following : FollowCheckResult.NotFollowing;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Instagram follow-check {ActorId}: истисно, Unknown ҳисоб карда шуд", actorId);
            return FollowCheckResult.Unknown;
        }
    }

    /// <summary>
    /// Рӯйхати постҳои охирин — барои интихоби пост дар UI-и қоидаи автоматизатсия
    /// (postScope=selected). VIDEO fields.media_url аксар вақт холист — thumbnail_url ҷои онро
    /// мегирад, то фронтенд лозим набошад ду майдонро худаш фарқ кунад.
    /// </summary>
    public Task<InstagramMediaPage> GetRecentMediaAsync(Channel channel, string? after, int limit, CancellationToken ct) =>
        GetMediaEdgeAsync(channel, "media", "id,media_type,media_url,thumbnail_url,permalink,caption,timestamp", after, limit, ct);

    /// <summary>
    /// Фазаи 20: сторисҳои фаъоли аккаунт (Meta танҳо 24 соати охирро медиҳад) — барои
    /// «сторисҳои интихобшуда» дар триггери «Ҷавоб ба сторис». Сторис caption надорад.
    /// </summary>
    public Task<InstagramMediaPage> GetActiveStoriesAsync(Channel channel, CancellationToken ct) =>
        GetMediaEdgeAsync(channel, "stories", "id,media_type,media_url,thumbnail_url,permalink,timestamp", after: null, limit: 100, ct);

    private async Task<InstagramMediaPage> GetMediaEdgeAsync(
        Channel channel, string edge, string fields, string? after, int limit, CancellationToken ct)
    {
        var credentials = GetCredentials(channel);
        var url = $"{GraphApiBaseUrl}/{GraphApiVersion}/{credentials.InstagramAccountId}/{edge}" +
                  $"?fields={fields}" +
                  $"&limit={limit}" +
                  (after is null ? "" : $"&after={Uri.EscapeDataString(after)}");

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);

        var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            var errorCode = TryGetErrorCode(body);
            if (errorCode == TokenExpiredErrorCode)
            {
                channel.RequiresReconnect = true;
                await db.SaveChangesAsync(ct);
            }

            logger.LogError("Instagram {Edge} GET хатогӣ: {StatusCode} {Body}", edge, (int)response.StatusCode, body);
            throw new GraphApiException(MetaErrorTranslator.Translate(body), body);
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        var items = new List<InstagramMediaItem>();
        if (doc.RootElement.TryGetProperty("data", out var dataEl))
        {
            foreach (var item in dataEl.EnumerateArray())
            {
                var mediaType = item.TryGetProperty("media_type", out var typeEl) ? typeEl.GetString() : null;
                var mediaUrl = item.TryGetProperty("media_url", out var urlEl) ? urlEl.GetString() : null;
                var thumbnailUrl = item.TryGetProperty("thumbnail_url", out var thumbEl) ? thumbEl.GetString() : null;

                // Санҷидашуда зинда (2026-09-14, reel-и воқеӣ): барои VIDEO, media_url файли
                // ХОМИ .mp4 аст (на расм) — Meta ба ин навъ ҳам media_url медиҳад (набудан-и он
                // тахмин нодуруст буд), пас <img src> хомӯшона намебарояд. thumbnail_url бояд
                // АВВАЛ санҷида шавад барои VIDEO/REELS; media_url танҳо барои IMAGE/CAROUSEL_ALBUM аст.
                var imageUrl = mediaType == "VIDEO" ? thumbnailUrl ?? mediaUrl : mediaUrl ?? thumbnailUrl;

                items.Add(new InstagramMediaItem(
                    Id: item.GetProperty("id").GetString()!,
                    MediaType: mediaType,
                    ImageUrl: imageUrl,
                    Permalink: item.TryGetProperty("permalink", out var permalinkEl) ? permalinkEl.GetString() : null,
                    Caption: item.TryGetProperty("caption", out var captionEl) ? captionEl.GetString() : null,
                    Timestamp: item.TryGetProperty("timestamp", out var tsEl) ? tsEl.GetString() : null));
            }
        }

        var nextCursor = doc.RootElement.TryGetProperty("paging", out var pagingEl) &&
            pagingEl.TryGetProperty("cursors", out var cursorsEl) &&
            cursorsEl.TryGetProperty("after", out var afterEl) &&
            pagingEl.TryGetProperty("next", out _)
            ? afterEl.GetString()
            : null;

        return new InstagramMediaPage(items, nextCursor);
    }

    /// <summary>
    /// Name, @username and picture link. Instagram answers only about a person who has written to
    /// the account — before that it is an error, logged, and Empty (ContactProfilePolicy asks again).
    /// </summary>
    public async Task<ContactProfile> GetContactProfileAsync(Channel channel, string contactExternalId, CancellationToken ct)
    {
        var credentials = GetCredentials(channel);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{GraphApiBaseUrl}/{GraphApiVersion}/{contactExternalId}?fields=name,username,profile_pic");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);

        var response = await httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning(
                "Instagram контакт {ContactExternalId} гирифта нашуд: {StatusCode} {Body}", contactExternalId, (int)response.StatusCode, body);
            return ContactProfile.Empty;
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        var username = doc.RootElement.TryGetProperty("username", out var usernameEl) ? usernameEl.GetString() : null;
        // "name" аксар вақт холист барои account-ҳои шахсӣ — username ҳамеша ҳаст (агар
        // "name" набошад, ҳамчун номи намоён истифода мешавад, вале ҳам алоҳида нигоҳ дошта мешавад).
        var name = doc.RootElement.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
        var avatarUrl = doc.RootElement.TryGetProperty("profile_pic", out var picEl) ? picEl.GetString() : null;

        return new ContactProfile(string.IsNullOrEmpty(name) ? username : name, avatarUrl, username);
    }

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

    private Task<string> PostToGraphApiAsync(Channel channel, InstagramCredentials credentials, string path, object payload, CancellationToken ct) =>
        PostToAbsoluteGraphApiPathAsync(channel, credentials, $"{credentials.InstagramAccountId}/{path}", payload, ct);

    /// <summary>
    /// Ҳамон PostToGraphApiAsync, вале барои path-ҳое, ки ба account id-и худи мо асос НАЁфтаанд
    /// (масалан "{comment-id}/replies" — comment id-и ягон корбар аст, на аккаунти мо).
    /// </summary>
    private Task<string> PostToAbsoluteGraphApiPathAsync(Channel channel, InstagramCredentials credentials, string path, object payload, CancellationToken ct) =>
        SendToGraphApiAsync(channel, credentials, HttpMethod.Post, path, payload, ct);

    /// <summary>Any Graph API call on this channel's token, with the shared failure handling below.</summary>
    private async Task<string> SendToGraphApiAsync(
        Channel channel, InstagramCredentials credentials, HttpMethod method, string path, object? payload, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, $"{GraphApiBaseUrl}/{GraphApiVersion}/{path}");
        if (payload is not null)
            request.Content = JsonContent.Create(payload);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);

        var response = await httpClient.SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
            return await response.Content.ReadAsStringAsync(ct);

        var responseBody = await response.Content.ReadAsStringAsync(ct);
        var errorCode = TryGetErrorCode(responseBody);

        if (errorCode == TokenExpiredErrorCode)
        {
            // Пеш аз ин танҳо notification-и Owner буд — нокомии токен дар UI намоён набуд, ва
            // паёмҳо хомӯшона рад мешуданд то касе бо дасти худ канал сохт. Ниг. report.
            channel.RequiresReconnect = true;
            await db.SaveChangesAsync(ct);
            await NotifyOwnersAsync(channel, "Instagram: токени дастрасӣ эътибор надорад ё тамом шудааст. Каналро санҷед.", ct);
        }
        else if (errorCode is RateLimitErrorCode or UserRateLimitErrorCode or SendApiRateLimitErrorCode)
            await NotifyOwnersAsync(channel, "Instagram: маҳдудияти дархост (rate limit) расид. Каналро санҷед.", ct);

        // МУВАҚҚАТӢ ТАШХИС (2026-08-25): се "Service temporarily unavailable" паиҳам — оё ин воқеан
        // rate limit аст? Агар сарлавҳаҳои поён холӣ бошанд, не — Meta худаш ҳеҷ маҳдудият надида.
        logger.LogError(
            "Instagram Graph API хатогӣ: {StatusCode} {Body} | rate-limit сарлавҳаҳо: {RateLimitHeaders}",
            (int)response.StatusCode, responseBody, MetaRateLimitHeaders.Describe(response.Headers) ?? "(нест)");
        throw new GraphApiException(MetaErrorTranslator.Translate(responseBody), responseBody);
    }

    private async Task NotifyOwnersAsync(Channel channel, string message, CancellationToken ct)
    {
        // A мизоҷ's channel is none of the company owners' business. The мизоҷ sees the problem on
        // their Accounts page (RequiresReconnect) and gets an email from InstagramTokenRefreshJob.
        if (channel.CustomerId is not null)
            return;

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
