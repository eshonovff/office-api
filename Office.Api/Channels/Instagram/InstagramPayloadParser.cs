using System.Text.Json;
using Office.Api.Data.Entities;

namespace Office.Api.Channels.Instagram;

/// <summary>
/// Табдили raw JSON-и webhook-и Instagram Messaging ба шаклҳои нормализатсияшуда — pure,
/// бе DB/HTTP. Шакли payload: <c>{"object":"instagram","entry":[{"id":"&lt;IG_ID&gt;",
/// "messaging":[{"sender":{"id":...},"message":{"mid":...,"text":...}}]}]}</c> — ҳамон
/// <c>entry[].messaging[]</c>-и Facebook (на <c>entry[].changes[]</c>-и WhatsApp), чунки
/// Instagram Messaging ба ҳамон Messenger Platform асос ёфтааст.
/// </summary>
public static class InstagramPayloadParser
{
    public static string? ExtractChannelExternalId(JsonElement payload) =>
        payload.TryGetProperty("entry", out var entryEl) && entryEl.ValueKind == JsonValueKind.Array && entryEl.GetArrayLength() > 0 &&
        entryEl[0].TryGetProperty("id", out var idEl)
            ? idEl.GetString()
            : null;

    /// <summary>Response-и `POST /{ig-id}/messages`: <c>{"recipient_id":"...","message_id":"..."}</c> (шакли Facebook-монанд).</summary>
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
            if (!messagingEvent.TryGetProperty("message", out var messageEl))
                continue;

            // Эхои паёми худи мо — набояд ҳамчун паёми воридотии мижоз сабт шавад.
            if (messageEl.TryGetProperty("is_echo", out var echoEl) && echoEl.ValueKind == JsonValueKind.True)
                continue;

            var senderId = messagingEvent.GetProperty("sender").GetProperty("id").GetString()!;
            var timestampMs = messagingEvent.GetProperty("timestamp").GetInt64();
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
                SentAt: DateTimeOffset.FromUnixTimeMilliseconds(timestampMs),
                // Ҳамон алгуи Facebook: URL-и CDN бо мӯҳлат, на media id — InstagramProvider.
                // DownloadMediaAsync онро мустақим GET мекунад.
                MediaExternalId: mediaUrl));
        }

        return result;
    }

    /// <summary>
    /// delivery.mids — ҳамон шакли Facebook, агар Instagram ин навъи event-ро фиристад (дар
    /// вақти навишта шудани ин парсер бо санҷиши зинда тасдиқ нашудааст — ниг. report). "read"
    /// (watermark, на per-message) ба монанди Facebook партофта мешавад.
    /// </summary>
    public static IReadOnlyList<ParsedStatusUpdate> ParseStatusUpdates(JsonElement payload)
    {
        var result = new List<ParsedStatusUpdate>();

        foreach (var messagingEvent in EnumerateMessagingEvents(payload))
        {
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

        // Ҷавоб ба сторис: паёми матнии оддӣ + reply_to.story (URL-и сторис ҳамчун контекст,
        // бо MessageType.StoryReply нишон дода мешавад — frontend аллакай инро ҳамчун
        // расм+матн намоиш медиҳад, ниг. MessageBubble.tsx).
        if (messageEl.TryGetProperty("reply_to", out var replyToEl) && replyToEl.TryGetProperty("story", out var storyEl))
        {
            var storyUrl = storyEl.TryGetProperty("url", out var storyUrlEl) ? storyUrlEl.GetString() : null;
            return (MessageType.StoryReply, text, storyUrl);
        }

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

            // story_mention: корбар account-ро дар сторисаш зикр кард (на "reply" — attachment-и
            // алоҳида, бе reply_to). MessageType-и ҷудогона надорем — StoryReply қасдан такрор
            // истифода мешавад (на партофта мешавад), ниг. report барои сабаб.
            case "story_mention":
                return (MessageType.StoryReply, text, url);

            // Reel/пости мубодилашуда: MessageType-и ҷудогона надорем — video бо нишонаи "[Reel]"
            // дар матн (frontend то ҳол бе тағйир видеои муқаррариро нишон медиҳад бо ин матн
            // дар зер — на badge-и воқеӣ, ин маҳдудияти қасдӣ аст, ниг. report).
            case "ig_reel":
            {
                var title = payloadEl.ValueKind == JsonValueKind.Object && payloadEl.TryGetProperty("title", out var titleEl)
                    ? titleEl.GetString()
                    : null;
                var reelBody = string.IsNullOrEmpty(title) ? "[Reel]" : $"[Reel] {title}";
                return (MessageType.Video, reelBody, url);
            }

            // Стикери дил (double-tap/heart sticker) — расм/видео надорад, барои сабти "навъи
            // маълум" (на паёми холӣ) матни собит истифода мешавад.
            case "like_heart":
                return (MessageType.Text, "❤️ (стикер)", null);

            default:
                return (MessageType.Text, text, url);
        }
    }
}
