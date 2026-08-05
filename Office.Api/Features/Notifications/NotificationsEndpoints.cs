using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Office.Api.Common;
using Office.Api.Data;

namespace Office.Api.Features.Notifications;

public static class NotificationsEndpoints
{
    public static IEndpointRouteBuilder MapNotificationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications").WithTags("Notifications").RequireAuthorization();

        group.MapGet("/", ListAsync);
        group.MapPost("/read", MarkReadAsync);

        return app;
    }

    private static async Task<IResult> ListAsync(ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var userId = principal.GetUserId();

        var notifications = await db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        return Results.Ok(notifications.Select(n => new NotificationDto(n.Id, n.Type, n.PayloadJson, n.IsRead, n.CreatedAt)));
    }

    private static async Task<IResult> MarkReadAsync(
        MarkNotificationsReadRequest? request,
        ClaimsPrincipal principal,
        AppDbContext db,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var query = db.Notifications.Where(n => n.UserId == userId && !n.IsRead);

        if (request?.NotificationIds is { Count: > 0 } ids)
            query = query.Where(n => ids.Contains(n.Id));

        var notifications = await query.ToListAsync(ct);
        foreach (var notification in notifications)
            notification.IsRead = true;

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
