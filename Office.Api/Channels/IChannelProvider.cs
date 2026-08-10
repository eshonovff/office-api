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

    Task SendMessageAsync(Channel channel, string conversationExternalId, string body, CancellationToken ct);

    /// <summary>Фиристодани шаблони тасдиқшуда (берун аз тирезаи 24-соата кор мекунад).</summary>
    Task SendTemplateAsync(
        Channel channel, string conversationExternalId, string templateName, string languageCode,
        IReadOnlyList<string> parameters, CancellationToken ct);

    Task MarkAsReadAsync(Channel channel, string messageExternalId, CancellationToken ct);

    Task<Stream> DownloadMediaAsync(Channel channel, string mediaExternalId, CancellationToken ct);

    /// <summary>Бор кардани медиа ба провайдер, барои дертар фиристодан. Media id-ро бармегардонад.</summary>
    Task<string> UploadMediaAsync(Channel channel, Stream content, string mimeType, string fileName, CancellationToken ct);

    /// <summary>Фиристодани паёми медиа (расм/видео/овоз/ҳуҷҷат) бо media id-и аллакай боркардашуда.</summary>
    Task SendMediaMessageAsync(
        Channel channel, string conversationExternalId, string mediaExternalId, MessageType type,
        string? caption, bool isVoiceNote, CancellationToken ct);

    /// <summary>Рӯйхати шаблонҳои тасдиқшудаи Meta барои ин канал.</summary>
    Task<IReadOnlyList<WhatsAppTemplateInfo>> GetApprovedTemplatesAsync(Channel channel, CancellationToken ct);
}
