using Office.Api.Data.Entities;
using Office.Api.Media;

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
    DateTimeOffset CreatedAt,
    // Composer-и frontend бо ҳамин рақамҳо пеш аз боркунӣ санҷад — як ҷои ягона барои
    // ҳудуди андозаи файл, на нусхаи дуюми дар frontend такрор навишташуда. Ниг. MediaUploadValidator.
    IReadOnlyList<MediaTypeLimit> MediaLimits);

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
    string? MediaDownloadError,
    IReadOnlyList<double>? WaveformPeaks,
    string? FailureReason,
    // Пайванди воқеии Reel/Post/Story-и мубодилашуда (ниг. Message.ExternalContentUrl) — на
    // дар матн, майдони алоҳида барои frontend, то "Кушодан дар Instagram" боэътимод кор кунад.
    string? ExternalContentUrl,
    string? ExternalContentKind)
{
    /// <summary>
    /// Ягона роҳи табдили Message ба шакли берунӣ — ҳам REST (GET/POST-и Conversations),
    /// ҳам realtime (WebhookProcessor, MediaDownloadJob, MediaSendJob) бояд ҳамин методро
    /// истифода баранд, то ду шакли гуногун барои як паём ҳеҷ гоҳ дур нашаванд.
    /// </summary>
    public static MessageDto FromEntity(Message m) => new(
        m.Id, m.ConversationId, m.Direction.ToString(), m.Type.ToString(), m.Body,
        m.MediaUrl is not null ? $"/api/messages/{m.Id}/media" : null,
        m.ExternalId, m.DeliveryStatus.ToString(), m.IsInternalNote, m.SentByUserId, m.SentByUserName,
        m.CreatedAt, m.MimeType, m.SizeBytes, m.OriginalFileName, m.VoiceDurationSeconds,
        m.ThumbnailUrl is not null ? $"/api/messages/{m.Id}/thumbnail" : null,
        m.MediaDeletedAt, m.MediaDownloadError,
        // 0-100 дар DB (smallint[], фишурда) → 0-1 дар DTO (тавре ки frontend интизор аст).
        m.WaveformPeaks?.Select(p => p / 100.0).ToList(),
        m.FailureReason, m.ExternalContentUrl, m.ExternalContentKind);
}

public record ConversationAssignmentEventDto(
    Guid Id,
    Guid? FromUserId,
    string? FromUserName,
    Guid? ToUserId,
    string? ToUserName,
    string Reason,
    DateTimeOffset CreatedAt)
{
    /// <summary>
    /// FromUserName/ToUserName — snapshot дар лаҳзаи таъин (на FromUser?.FullName /
    /// ToUser?.FullName), ҳамон сабабе, ки MessageDto.SentByUserName-ро водор кард: нест
    /// кардани корбар (FK → SET NULL) набояд таърихи таъинотро вайрон кунад.
    /// </summary>
    public static ConversationAssignmentEventDto FromEntity(ConversationAssignmentEvent e) => new(
        e.Id, e.FromUserId, e.FromUserName, e.ToUserId, e.ToUserName, e.Reason.ToString(), e.CreatedAt);
}

public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

public record SendMessageRequest(
    string? Body,
    string? TemplateName,
    string? TemplateLanguage,
    IReadOnlyList<string>? TemplateParameters,
    bool IsInternalNote = false);

public record UpdateConversationRequest(string? Status, Guid? AssignedTo);
