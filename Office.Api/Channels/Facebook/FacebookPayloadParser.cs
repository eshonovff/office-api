using System.Text.Json;
using Office.Api.Data.Entities;

namespace Office.Api.Channels.Facebook;

/// <summary>
/// Табдили raw JSON-и webhook-и Facebook Messenger ба шаклҳои нормализатсияшуда — pure,
/// бе DB/HTTP. Шакли payload: <c>{"object":"page","entry":[{"id":"&lt;PAGE_ID&gt;",
/// "messaging":[{"sender":{"id":...},"message":{"mid":...,"text":...}}]}]}</c> — на
/// <c>entry[].changes[]</c>-и WhatsApp/Instagram, балки <c>entry[].messaging[]</c>.
/// </summary>
public static class FacebookPayloadParser
{
    public static string? ExtractChannelExternalId(JsonElement payload) =>
        payload.TryGetProperty("entry", out var entryEl) && entryEl.ValueKind == JsonValueKind.Array && entryEl.GetArrayLength() > 0 &&
        entryEl[0].TryGetProperty("id", out var idEl)
            ? idEl.GetString()
            : null;

    /// <summary>Response-и `POST /{page-id}/messages`: <c>{"recipient_id":"...","message_id":"mid.xxx"}</c>.</summary>
    public static string? ExtractSentMessageId(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);
        return doc.RootElement.TryGetProperty("message_id", out var idEl) ? idEl.GetString() : null;
    }

    public static IReadOnlyList<ParsedWebhookMessage> ParseMessages(JsonElement payload)
    {
        var result = new List<ParsedWebhookMessage>();

        foreach (var messagingEvent in EnumerateMessagingEvents(payload))
        {
            var senderId = messagingEvent.GetProperty("sender").GetProperty("id").GetString()!;
            var timestampMs = messagingEvent.GetProperty("timestamp").GetInt64();
            var sentAt = DateTimeOffset.FromUnixTimeMilliseconds(timestampMs);

            if (messagingEvent.TryGetProperty("message", out var messageEl))
            {
                // Эхои паёми худи мо (агар Page аз чанд ҷо идора шавад ҳам webhook мефиристад) —
                // набояд ҳамчун паёми воридотии мижоз сабт шавад.
                if (messageEl.TryGetProperty("is_echo", out var echoEl) && echoEl.ValueKind == JsonValueKind.True)
                    continue;

                var mid = messageEl.GetProperty("mid").GetString()!;
                var (type, body, mediaUrl) = MapMessageContent(messageEl);

                result.Add(new ParsedWebhookMessage(
                    ConversationExternalId: senderId,
                    ContactName: null,
                    ContactAvatarUrl: null,
                    MessageExternalId: mid,
                    Direction: MessageDirection.Inbound,
                    Type: type,
                    Body: body,
                    MediaUrl: null,
                    SentAt: sentAt,
                    // MediaExternalId дар ин ҷо URL-и CDN аст (бо мӯҳлат), на media id-и WhatsApp-монанд —
                    // FacebookProvider.DownloadMediaAsync онро мустақим GET мекунад, бе қадами ҳалли ID.
                    MediaExternalId: mediaUrl));

                continue;
            }

            if (messagingEvent.TryGetProperty("postback", out var postbackEl))
                result.Add(ParsePostback(senderId, timestampMs, sentAt, postbackEl));
        }

        return result;
    }

    public static IReadOnlyList<ParsedStatusUpdate> ParseStatusUpdates(JsonElement payload)
    {
        var result = new List<ParsedStatusUpdate>();

        foreach (var messagingEvent in EnumerateMessagingEvents(payload))
        {
            // "read" (watermark, на per-message) қасдан партофта мешавад — ParsedStatusUpdate
            // як MessageExternalId-и мушаххасро талаб мекунад, "то ин вақт ҳама хонда шуд"-ро
            // ифода карда наметавонад.
            if (!messagingEvent.TryGetProperty("delivery", out var deliveryEl) ||
                !deliveryEl.TryGetProperty("mids", out var midsEl) || midsEl.ValueKind != JsonValueKind.Array)
                continue;

            var updatedAt = DateTimeOffset.FromUnixTimeMilliseconds(messagingEvent.GetProperty("timestamp").GetInt64());

            foreach (var midEl in midsEl.EnumerateArray())
            {
                var mid = midEl.GetString();
                if (mid is not null)
                    result.Add(new ParsedStatusUpdate(mid, MessageDeliveryStatus.Delivered, updatedAt));
            }
        }

        return result;
    }

    private static ParsedWebhookMessage ParsePostback(string senderId, long timestampMs, DateTimeOffset sentAt, JsonElement postbackEl)
    {
        var body = postbackEl.TryGetProperty("title", out var titleEl) ? titleEl.GetString()
            : postbackEl.TryGetProperty("payload", out var payloadEl) ? payloadEl.GetString() : null;

        return new ParsedWebhookMessage(
            ConversationExternalId: senderId,
            ContactName: null,
            ContactAvatarUrl: null,
            // postback-ҳо mid надоранд — id-и синтетикӣ аз sender+timestamp: ҳамон webhook такрор
            // расад ҳам ҳамин id мебарояд, пас MessageIdempotencyPlanner онро дубора сабт намекунад.
            MessageExternalId: $"postback:{senderId}:{timestampMs}",
            Direction: MessageDirection.Inbound,
            Type: MessageType.Text,
            Body: body,
            MediaUrl: null,
            SentAt: sentAt);
    }

    private static IEnumerable<JsonElement> EnumerateMessagingEvents(JsonElement payload)
    {
        if (!payload.TryGetProperty("entry", out var entryEl) || entryEl.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var entry in entryEl.EnumerateArray())
        {
            if (!entry.TryGetProperty("messaging", out var messagingEl) || messagingEl.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var evt in messagingEl.EnumerateArray())
                yield return evt;
        }
    }

    private static (MessageType Type, string? Body, string? MediaUrl) MapMessageContent(JsonElement messageEl)
    {
        var text = messageEl.TryGetProperty("text", out var textEl) ? textEl.GetString() : null;

        if (!messageEl.TryGetProperty("attachments", out var attachmentsEl) || attachmentsEl.ValueKind != JsonValueKind.Array ||
            attachmentsEl.GetArrayLength() == 0)
        {
            return (MessageType.Text, text, null);
        }

        var attachment = attachmentsEl[0];
        var attachmentType = attachment.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
        var payloadEl = attachment.TryGetProperty("payload", out var pEl) ? pEl : default;
        var url = payloadEl.ValueKind == JsonValueKind.Object && payloadEl.TryGetProperty("url", out var urlEl) ? urlEl.GetString() : null;

        switch (attachmentType)
        {
            case "image":
                return (MessageType.Image, text, url);
            case "video":
                return (MessageType.Video, text, url);
            case "audio":
                return (MessageType.Audio, text, url);
            case "file":
                return (MessageType.File, text, url);

            // Reel/пости мубодилашуда (на ig_reel-и Instagram — Facebook навъи худро дорад,
            // "reel"). MessageType-и ҷудогона надорем — video бо нишонаи "[Reel]" дар матн,
            // ҳамон алгуи Instagram (ниг. InstagramPayloadParser барои сабаб).
            case "reel":
            {
                var title = payloadEl.ValueKind == JsonValueKind.Object && payloadEl.TryGetProperty("title", out var titleEl)
                    ? titleEl.GetString()
                    : null;
                var reelBody = string.IsNullOrEmpty(title) ? "[Reel]" : $"[Reel] {title}";
                return (MessageType.Video, reelBody, url);
            }

            // Стикери дил (double-tap/heart sticker) — расм/видео надорад, ҳамон алгуи Instagram.
            case "like_heart":
                return (MessageType.Text, "❤️ (стикер)", null);

            default:
                return (MessageType.Text, text, url);
        }
    }
}
