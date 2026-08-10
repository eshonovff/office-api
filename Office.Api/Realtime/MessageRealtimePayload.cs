using Office.Api.Data.Entities;

namespace Office.Api.Realtime;

/// <summary>
/// Шакли ягонаи паём барои event-ҳои realtime (MessageReceived — воридотӣ ва боркунии
/// баъдинаи медиа; MessageSent — навсозии статуси расониш) — pure, бе DB/HTTP, то тавон
/// шаклбандии URL-ро (масалан роҳи захира → endpoint) ҷудо тест кард.
/// </summary>
public record MessageRealtimePayload(
    Guid Id,
    Guid ConversationId,
    string Direction,
    string Type,
    string? Body,
    string? MediaUrl,
    string? MimeType,
    long? SizeBytes,
    string? OriginalFileName,
    int? VoiceDurationSeconds,
    string? ThumbnailUrl,
    string? MediaDownloadError,
    string? ExternalId,
    string DeliveryStatus,
    DateTimeOffset CreatedAt)
{
    public static MessageRealtimePayload FromEntity(Message message) => new(
        message.Id,
        message.ConversationId,
        message.Direction.ToString(),
        message.Type.ToString(),
        message.Body,
        message.MediaUrl is not null ? $"/api/messages/{message.Id}/media" : null,
        message.MimeType,
        message.SizeBytes,
        message.OriginalFileName,
        message.VoiceDurationSeconds,
        message.ThumbnailUrl is not null ? $"/api/messages/{message.Id}/thumbnail" : null,
        message.MediaDownloadError,
        message.ExternalId,
        message.DeliveryStatus.ToString(),
        message.CreatedAt);
}
