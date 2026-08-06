using System.Text.Json;
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

    /// <summary>Табдили JSON-и хоми webhook ба паёмҳои нормализатсияшуда.</summary>
    Task<IReadOnlyList<ParsedWebhookMessage>> ParseWebhookAsync(Channel channel, JsonElement payload, CancellationToken ct);

    Task SendMessageAsync(Channel channel, string conversationExternalId, string body, CancellationToken ct);

    Task MarkAsReadAsync(Channel channel, string messageExternalId, CancellationToken ct);

    Task<Stream> DownloadMediaAsync(Channel channel, string mediaExternalId, CancellationToken ct);
}
