using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Features.Notifications;

namespace Office.Api.Realtime;

public interface INotificationService
{
    Task PushAsync(Guid userId, string type, object payload, CancellationToken ct);
}

public class NotificationService(AppDbContext db, IHubContext<InboxHub> hubContext) : INotificationService
{
    public async Task PushAsync(Guid userId, string type, object payload, CancellationToken ct)
    {
        var notification = new Notification
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Type = type,
            PayloadJson = JsonSerializer.Serialize(payload),
            IsRead = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Notifications.Add(notification);
        await db.SaveChangesAsync(ct);

        var dto = new NotificationDto(notification.Id, notification.Type, notification.PayloadJson, notification.IsRead, notification.CreatedAt);
        await hubContext.Clients.Group(InboxHub.UserGroupName(userId)).SendAsync("NotificationReceived", dto, ct);
    }
}
