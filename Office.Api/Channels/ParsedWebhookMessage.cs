using Office.Api.Data.Entities;

namespace Office.Api.Channels;

/// <summary>
/// Шакли нормализатсияшудаи як паём аз webhook, новобаста аз provider.
/// </summary>
public record ParsedWebhookMessage(
    string ConversationExternalId,
    string? ContactName,
    string? ContactAvatarUrl,
    string MessageExternalId,
    MessageDirection Direction,
    MessageType Type,
    string? Body,
    string? MediaUrl,
    DateTimeOffset SentAt);
