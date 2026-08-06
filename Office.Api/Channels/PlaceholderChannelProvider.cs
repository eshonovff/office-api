using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Office.Api.Data.Entities;

namespace Office.Api.Channels;

/// <summary>
/// Ҷойгузини муваққатӣ то амалисозии воқеӣ дар фазаи 5 (WhatsApp) ва
/// фазаи 7 (Instagram/Facebook). Ягон мантиқи хосси Meta/WhatsApp дар
/// ин ҷо нест — танҳо барои санҷиши infrastructure-и webhook (имзо,
/// idempotency, Hangfire, upsert-и conversation/message) дар фазаи 4
/// истифода мешавад. Формати payload генералӣ аст, на воқеии Meta.
///
/// Шакли интизории payload:
/// {
///   "messages": [
///     { "conversationExternalId": "...", "contactName": "...",
///       "messageExternalId": "...", "direction": "inbound",
///       "type": "text", "body": "...", "sentAt": "..." }
///   ]
/// }
/// </summary>
public class PlaceholderChannelProvider(IConfiguration configuration) : IChannelProvider
{
    public bool VerifyWebhookToken(string verifyToken)
    {
        var expected = configuration["Webhooks:VerifyToken"];
        return !string.IsNullOrEmpty(expected) && verifyToken == expected;
    }

    public Task<IReadOnlyList<ParsedWebhookMessage>> ParseWebhookAsync(Channel channel, JsonElement payload, CancellationToken ct)
    {
        var result = new List<ParsedWebhookMessage>();

        if (!payload.TryGetProperty("messages", out var messagesElement) || messagesElement.ValueKind != JsonValueKind.Array)
            return Task.FromResult<IReadOnlyList<ParsedWebhookMessage>>(result);

        foreach (var item in messagesElement.EnumerateArray())
        {
            var direction = item.TryGetProperty("direction", out var directionEl) &&
                             Enum.TryParse<MessageDirection>(directionEl.GetString(), ignoreCase: true, out var parsedDirection)
                ? parsedDirection
                : MessageDirection.Inbound;

            var type = item.TryGetProperty("type", out var typeEl) &&
                       Enum.TryParse<MessageType>(typeEl.GetString(), ignoreCase: true, out var parsedType)
                ? parsedType
                : MessageType.Text;

            var sentAt = item.TryGetProperty("sentAt", out var sentAtEl) && sentAtEl.TryGetDateTimeOffset(out var parsedSentAt)
                ? parsedSentAt
                : DateTimeOffset.UtcNow;

            result.Add(new ParsedWebhookMessage(
                ConversationExternalId: item.GetProperty("conversationExternalId").GetString()!,
                ContactName: item.TryGetProperty("contactName", out var nameEl) ? nameEl.GetString() : null,
                ContactAvatarUrl: item.TryGetProperty("contactAvatarUrl", out var avatarEl) ? avatarEl.GetString() : null,
                MessageExternalId: item.GetProperty("messageExternalId").GetString()!,
                Direction: direction,
                Type: type,
                Body: item.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() : null,
                MediaUrl: item.TryGetProperty("mediaUrl", out var mediaEl) ? mediaEl.GetString() : null,
                SentAt: sentAt));
        }

        return Task.FromResult<IReadOnlyList<ParsedWebhookMessage>>(result);
    }

    public Task SendMessageAsync(Channel channel, string conversationExternalId, string body, CancellationToken ct)
        => throw new NotImplementedException("SendMessage дар фазаи 5 (WhatsApp) ва фазаи 7 (Instagram/Facebook) амалӣ мешавад.");

    public Task MarkAsReadAsync(Channel channel, string messageExternalId, CancellationToken ct)
        => throw new NotImplementedException("MarkAsRead дар фазаи 5/7 амалӣ мешавад.");

    public Task<Stream> DownloadMediaAsync(Channel channel, string mediaExternalId, CancellationToken ct)
        => throw new NotImplementedException("DownloadMedia дар фазаи 5/7 амалӣ мешавад.");
}
