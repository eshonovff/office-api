namespace Office.Api.Features.Notifications;

public record NotificationDto(Guid Id, string Type, string? PayloadJson, bool IsRead, DateTimeOffset CreatedAt);

public record MarkNotificationsReadRequest(IReadOnlyList<Guid>? NotificationIds);
