using System.Text.Json;
using Office.Api.Channels.WhatsApp;
using Office.Api.Data.Entities;

namespace Office.Api.Channels;

/// <summary>
/// Абстраксияи канал. Амалисозии воқеӣ барои ҳар provider дар
/// фазаи 5 (WhatsApp) ва фазаи 7 (Instagram/Facebook) меояд.
/// </summary>
public interface IChannelProvider
{
    /// <summary>Санҷиши `hub.verify_token` ҳангоми GET /webhooks/{provider}.</summary>
    bool VerifyWebhookToken(string verifyToken);

    /// <summary>Аз raw payload-и webhook, ExternalId-и канал (масалан phone_number_id)-ро мебарорад.</summary>
    string? ExtractChannelExternalId(JsonElement payload);

    /// <summary>Табдили JSON-и хоми webhook ба паёмҳои нормализатсияшуда.</summary>
    Task<IReadOnlyList<ParsedWebhookMessage>> ParseWebhookAsync(Channel channel, JsonElement payload, CancellationToken ct);

    /// <summary>Табдили JSON-и хоми webhook ба навсозиҳои статуси расониш (sent/delivered/read/failed).</summary>
    Task<IReadOnlyList<ParsedStatusUpdate>> ParseStatusUpdatesAsync(Channel channel, JsonElement payload, CancellationToken ct);

    /// <summary>
    /// Wamid-ро бармегардонад (агар дастрас бошад), то навсозиҳои статус (delivered/read) ба паём мувофиқ оянд.
    /// <paramref name="messageTag"/> — Facebook/Instagram-хос (масалан <c>MessengerTags.HumanAgent</c>): берун
    /// аз тирезаи муқаррарӣ, вале дар доираи дарозкунии тег иҷозатдодашуда мефиристад. WhatsApp мафҳуми
    /// тег надорад — ҳамеша null мегузарад ва амалисозии он инро нодида мегирад.
    /// </summary>
    Task<string?> SendMessageAsync(Channel channel, string conversationExternalId, string body, string? messageTag, CancellationToken ct);

    /// <summary>Фиристодани шаблони тасдиқшуда (берун аз тирезаи 24-соата кор мекунад). Wamid-ро бармегардонад.</summary>
    Task<string?> SendTemplateAsync(
        Channel channel, string conversationExternalId, string templateName, string languageCode,
        IReadOnlyList<string> parameters, CancellationToken ct);

    Task MarkAsReadAsync(Channel channel, string messageExternalId, CancellationToken ct);

    /// <summary>
    /// ContentType-и ҳамон response — MediaDownloadJob инро ҳам барои санҷиши "воқеан медиа аст,
    /// на HTML/JSON-и хатогӣ бо 200 OK" истифода мебарад, ҳам (агар message.MimeType холӣ бошад,
    /// масалан Facebook/Instagram, ки webhook mime_type намедиҳанд) ҳамчун сарчашмаи он.
    /// </summary>
    Task<DownloadedMedia> DownloadMediaAsync(Channel channel, string mediaExternalId, CancellationToken ct);

    /// <summary>Бор кардани медиа ба провайдер, барои дертар фиристодан. Media id-ро бармегардонад.</summary>
    Task<string> UploadMediaAsync(Channel channel, Stream content, string mimeType, string fileName, CancellationToken ct);

    /// <summary>
    /// Фиристодани паёми медиа (расм/видео/овоз/ҳуҷҷат) бо media id-и аллакай боркардашуда. Wamid-ро
    /// бармегардонад (агар дастрас бошад). <paramref name="messageTag"/> — ниг. <see cref="SendMessageAsync"/>.
    /// </summary>
    Task<string?> SendMediaMessageAsync(
        Channel channel, string conversationExternalId, string mediaExternalId, MessageType type,
        string? caption, bool isVoiceNote, string? messageTag, CancellationToken ct);

    /// <summary>Рӯйхати шаблонҳои тасдиқшудаи Meta барои ин канал.</summary>
    Task<IReadOnlyList<WhatsAppTemplateInfo>> GetApprovedTemplatesAsync(Channel channel, CancellationToken ct);

    /// <summary>
    /// Ном/username ва URL-и расми профили мижоз — WhatsApp инро дар худи webhook медиҳад
    /// (<see cref="ParsedWebhookMessage.ContactName"/>), пас WhatsAppProvider ҳамеша (null, null)
    /// бармегардонад (дархости иловагӣ лозим нест). Facebook/Instagram чунин майдонро дар
    /// webhook намефиристанд — дархости алоҳида (танҳо як маротиба, ҳангоми сохтани conversation-и
    /// нав, на барои ҳар паём) лозим аст.
    /// </summary>
    Task<ContactProfile> GetContactProfileAsync(Channel channel, string contactExternalId, CancellationToken ct);
}

/// <summary>
/// AvatarUrl метавонад мӯҳлатнок бошад (URL-и CDN-и Meta) — ҳоло бе зеркашӣ/навсозии даврӣ
/// захира мешавад (танҳо як маротиба, ҳангоми сохтани conversation); баъд аз мӯҳлат вайрон
/// шуданаш маълум аст ва қасдан ҳал нашудааст (ниг. эзоҳи commit).
/// </summary>
public record ContactProfile(string? Name, string? AvatarUrl, string? Username = null)
{
    public static readonly ContactProfile Empty = new(null, null);
}

/// <summary>Content-ро баста мекунад (IDisposable — caller онро дар <c>await using</c> мегирад) бо ContentType-и воқеии response.</summary>
public sealed record DownloadedMedia(Stream Content, string? ContentType);
