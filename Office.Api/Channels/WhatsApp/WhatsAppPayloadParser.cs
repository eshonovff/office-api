using System.Text.Json;
using Office.Api.Data.Entities;

namespace Office.Api.Channels.WhatsApp;

/// <summary>
/// Табдили raw JSON-и webhook-и Meta ба шаклҳои нормализатсияшуда — pure, бе DB/HTTP,
/// то тавон онро бе тамоми DI-и WhatsAppProvider тест кард.
/// </summary>
public static class WhatsAppPayloadParser
{
    public static string? ExtractChannelExternalId(JsonElement payload) =>
        TryGetFirstChangeValue(payload, out var value) &&
        value.TryGetProperty("metadata", out var metadata) &&
        metadata.TryGetProperty("phone_number_id", out var idEl)
            ? idEl.GetString()
            : null;

    public static IReadOnlyList<ParsedWebhookMessage> ParseMessages(JsonElement payload)
    {
        var result = new List<ParsedWebhookMessage>();

        if (!TryGetFirstChangeValue(payload, out var value) ||
            !value.TryGetProperty("messages", out var messagesEl) || messagesEl.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        var contactNamesByWaId = new Dictionary<string, string>();
        if (value.TryGetProperty("contacts", out var contactsEl) && contactsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var contact in contactsEl.EnumerateArray())
            {
                var waId = contact.TryGetProperty("wa_id", out var waIdEl) ? waIdEl.GetString() : null;
                var name = contact.TryGetProperty("profile", out var profile) && profile.TryGetProperty("name", out var nameEl)
                    ? nameEl.GetString()
                    : null;

                if (waId is not null && name is not null)
                    contactNamesByWaId[waId] = name;
            }
        }

        foreach (var message in messagesEl.EnumerateArray())
        {
            var from = message.GetProperty("from").GetString()!;
            var messageId = message.GetProperty("id").GetString()!;
            var sentAt = DateTimeOffset.FromUnixTimeSeconds(long.Parse(message.GetProperty("timestamp").GetString()!));
            var typeStr = message.GetProperty("type").GetString()!;

            var (type, body, mediaExternalId, mimeType, fileName) = MapMessageContent(typeStr, message);

            result.Add(new ParsedWebhookMessage(
                ConversationExternalId: from,
                ContactName: contactNamesByWaId.GetValueOrDefault(from),
                ContactAvatarUrl: null,
                MessageExternalId: messageId,
                Direction: MessageDirection.Inbound,
                Type: type,
                Body: body,
                MediaUrl: null,
                SentAt: sentAt,
                MediaExternalId: mediaExternalId,
                MimeType: mimeType,
                OriginalFileName: fileName));
        }

        return result;
    }

    public static IReadOnlyList<ParsedStatusUpdate> ParseStatusUpdates(JsonElement payload)
    {
        var result = new List<ParsedStatusUpdate>();

        if (!TryGetFirstChangeValue(payload, out var value) ||
            !value.TryGetProperty("statuses", out var statusesEl) || statusesEl.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var status in statusesEl.EnumerateArray())
        {
            var messageId = status.GetProperty("id").GetString()!;
            var updatedAt = DateTimeOffset.FromUnixTimeSeconds(long.Parse(status.GetProperty("timestamp").GetString()!));

            var deliveryStatus = status.GetProperty("status").GetString() switch
            {
                "sent" => MessageDeliveryStatus.Sent,
                "delivered" => MessageDeliveryStatus.Delivered,
                "read" => MessageDeliveryStatus.Read,
                "failed" => MessageDeliveryStatus.Failed,
                _ => (MessageDeliveryStatus?)null,
            };

            if (deliveryStatus is not null)
                result.Add(new ParsedStatusUpdate(messageId, deliveryStatus.Value, updatedAt));
        }

        return result;
    }

    private static (MessageType Type, string? Body, string? MediaExternalId, string? MimeType, string? FileName) MapMessageContent(
        string typeStr, JsonElement message) =>
        typeStr switch
        {
            "text" => (MessageType.Text, message.GetProperty("text").GetProperty("body").GetString(), null, null, null),
            "image" => (MessageType.Image, GetCaption(message, "image"), GetMediaId(message, "image"), GetMimeType(message, "image"), null),
            "video" => (MessageType.Video, GetCaption(message, "video"), GetMediaId(message, "video"), GetMimeType(message, "video"), null),
            "audio" => (MessageType.Audio, null, GetMediaId(message, "audio"), GetMimeType(message, "audio"), null),
            "document" => (MessageType.File, GetCaption(message, "document"), GetMediaId(message, "document"),
                GetMimeType(message, "document"), GetFileName(message)),
            "location" => (MessageType.Location, FormatLocation(message), null, null, null),
            "contacts" => (MessageType.Contact, FormatContacts(message), null, null, null),
            _ => (MessageType.Text, $"[Навъи дастгирӣнашуда: {typeStr}]", null, null, null),
        };

    private static string? GetCaption(JsonElement message, string field) =>
        message.TryGetProperty(field, out var el) && el.TryGetProperty("caption", out var captionEl)
            ? captionEl.GetString()
            : null;

    private static string? GetMediaId(JsonElement message, string field) =>
        message.TryGetProperty(field, out var el) && el.TryGetProperty("id", out var idEl)
            ? idEl.GetString()
            : null;

    private static string? GetMimeType(JsonElement message, string field) =>
        message.TryGetProperty(field, out var el) && el.TryGetProperty("mime_type", out var mimeEl)
            ? mimeEl.GetString()
            : null;

    private static string? GetFileName(JsonElement message) =>
        message.TryGetProperty("document", out var el) && el.TryGetProperty("filename", out var nameEl)
            ? nameEl.GetString()
            : null;

    private static string FormatLocation(JsonElement message)
    {
        var location = message.GetProperty("location");
        var lat = location.GetProperty("latitude").GetDouble();
        var lng = location.GetProperty("longitude").GetDouble();
        var name = location.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;

        return name is not null ? $"{name} ({lat}, {lng})" : $"{lat}, {lng}";
    }

    private static string FormatContacts(JsonElement message)
    {
        var names = message.GetProperty("contacts").EnumerateArray()
            .Select(c => c.TryGetProperty("name", out var n) && n.TryGetProperty("formatted_name", out var fn) ? fn.GetString() : null)
            .Where(n => n is not null);

        return string.Join(", ", names);
    }

    private static bool TryGetFirstChangeValue(JsonElement payload, out JsonElement value)
    {
        value = default;

        if (!payload.TryGetProperty("entry", out var entryEl) || entryEl.ValueKind != JsonValueKind.Array || entryEl.GetArrayLength() == 0)
            return false;

        if (!entryEl[0].TryGetProperty("changes", out var changesEl) || changesEl.ValueKind != JsonValueKind.Array || changesEl.GetArrayLength() == 0)
            return false;

        return changesEl[0].TryGetProperty("value", out value);
    }
}
