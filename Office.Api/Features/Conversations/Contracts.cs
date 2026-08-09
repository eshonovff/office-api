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
    DateTimeOffset CreatedAt);

public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

public record SendMessageRequest(
    string? Body,
    string? TemplateName,
    string? TemplateLanguage,
    IReadOnlyList<string>? TemplateParameters);

public record UpdateConversationRequest(string? Status, Guid? AssignedTo);
