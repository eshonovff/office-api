using Office.Api.Data.Entities;

namespace Office.Api.Features.Conversations;

public record ConversationListItem(
    Guid Id,
    Guid ChannelId,
    string ChannelType,
    string ChannelName,
    string ExternalId,
    string? ContactName,
    string? ContactAvatarUrl,
    string Status,
    Guid? AssignedTo,
    string? AssignedToName,
    DateTimeOffset? LastMessageAt,
    int UnreadCount,
    DateTimeOffset? WindowExpiresAt,
    DateTimeOffset CreatedAt);

public record ConversationDetail(
    Guid Id,
    Guid ChannelId,
    string ChannelType,
    string ChannelName,
    string ExternalId,
    string? ContactName,
    string? ContactAvatarUrl,
    string Status,
    Guid? AssignedTo,
    string? AssignedToName,
    DateTimeOffset? LastMessageAt,
    int UnreadCount,
    DateTimeOffset? WindowExpiresAt,
    DateTimeOffset CreatedAt);

public record MessageDto(
    Guid Id,
    Guid ConversationId,
    string Direction,
    string Type,
    string? Body,
    string? MediaUrl,
    string? ExternalId,
    string DeliveryStatus,
    bool IsInternalNote,
    Guid? SentByUserId,
    string? SentByUserName,
    DateTimeOffset CreatedAt,
    string? MimeType,
    long? SizeBytes,
    string? OriginalFileName,
    int? VoiceDurationSeconds,
    string? ThumbnailUrl,
    DateTimeOffset? MediaDeletedAt,
    string? MediaDownloadError)
{
    /// <summary>
    /// Ягона роҳи табдили Message ба шакли берунӣ — ҳам REST (GET/POST-и Conversations),
    /// ҳам realtime (WebhookProcessor, MediaDownloadJob) бояд ҳамин методро истифода
    /// баранд, то ду шакли гуногун барои як паём ҳеҷ гоҳ дур нашаванд.
    /// </summary>
    public static MessageDto FromEntity(Message m) => new(
        m.Id, m.ConversationId, m.Direction.ToString(), m.Type.ToString(), m.Body,
        m.MediaUrl is not null ? $"/api/messages/{m.Id}/media" : null,
        m.ExternalId, m.DeliveryStatus.ToString(), m.IsInternalNote, m.SentByUserId, m.SentByUser?.FullName,
        m.CreatedAt, m.MimeType, m.SizeBytes, m.OriginalFileName, m.VoiceDurationSeconds,
        m.ThumbnailUrl is not null ? $"/api/messages/{m.Id}/thumbnail" : null,
        m.MediaDeletedAt, m.MediaDownloadError);
}

public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

public record SendMessageRequest(
    string? Body,
    string? TemplateName,
    string? TemplateLanguage,
    IReadOnlyList<string>? TemplateParameters);

public record UpdateConversationRequest(string? Status, Guid? AssignedTo);
