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
    string? ExternalContentKind = null,
    /// <summary>
    /// Фазаи 12: агар ин паём аз пахши тугмаи postback бошад (на матни оддӣ), payload-и
    /// барномасози тугма ин ҷо аст (Body = title-и тугма, барои намоиш дар inbox). Тасдиқшуда
    /// бо ҳуҷҷати расмии Meta (2026-09-15): messaging[].postback:{title,payload} — ҳамон шакли
    /// Facebook (Instagram ҳамон Messenger Platform-ро истифода мебарад).
    /// </summary>
    string? PostbackPayload = null,
    /// <summary>
    /// Фазаи 20: Instagram — ҷавоб ба сторис ё қайд дар сторис (ҳарду MessageType.StoryReply
    /// доранд; триггерҳои флоу онҳоро ҷудо мекунанд). StoryId танҳо дар ҷавоб ҳаст — Meta дар
    /// story_mention id намефиристад.
    /// </summary>
    StoryEventKind? Story = null,
    string? StoryId = null);

public enum StoryEventKind
{
    Reply,
    Mention,
}
