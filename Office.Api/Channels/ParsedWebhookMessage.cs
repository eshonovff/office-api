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
    DateTimeOffset SentAt,
    /// <summary>Media ID-и провайдер (мас. Meta) — агар набошад, ParseWebhookAsync баъд ин баркашида мешавад.</summary>
    string? MediaExternalId = null,
    string? MimeType = null,
    string? OriginalFileName = null,
    /// <summary>Пайванди воқеии Reel/Post/Story (ниг. Message.ExternalContentUrl) — ҳеҷ гоҳ зеркашӣ намешавад.</summary>
    string? ExternalContentUrl = null,
    /// <summary>"Reel" | "Post" | "Story" — танҳо вақте ExternalContentUrl пур аст.</summary>
    string? ExternalContentKind = null);
