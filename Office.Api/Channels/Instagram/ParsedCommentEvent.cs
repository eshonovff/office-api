namespace Office.Api.Channels.Instagram;

/// <summary>Шакли нормализатсияшудаи webhook-и коментарии Instagram — ниг. InstagramPayloadParser.ParseCommentEvents.</summary>
/// <param name="ParentId">Set when this comment is a reply to another comment (a thread).</param>
/// <param name="OccurredAt">When Meta says it happened (entry.time); null in older test fixtures.</param>
public record ParsedCommentEvent(
    string CommentId,
    string ActorExternalId,
    string? ActorUsername,
    string Text,
    string? MediaId,
    string? ParentId = null,
    DateTimeOffset? OccurredAt = null);
