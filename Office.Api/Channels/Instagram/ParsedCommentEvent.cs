namespace Office.Api.Channels.Instagram;

/// <summary>Шакли нормализатсияшудаи webhook-и коментарии Instagram — ниг. InstagramPayloadParser.TryParseCommentEvent.</summary>
public record ParsedCommentEvent(
    string CommentId,
    string ActorExternalId,
    string? ActorUsername,
    string Text,
    string? MediaId);
