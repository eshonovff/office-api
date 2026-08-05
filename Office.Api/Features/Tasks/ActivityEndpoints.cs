using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Common;
using Office.Api.Data;
using Permissions = Office.Api.Auth.Permissions;

namespace Office.Api.Features.Tasks;

public static class ActivityEndpoints
{
    public static IEndpointRouteBuilder MapActivityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/tasks/{taskId:guid}/activity", ListAsync)
            .WithTags("Activity")
            .RequirePermission(Permissions.Tasks.View)
            .WithSummary("Таърихи тағйироти таск (сабти автоматӣ)")
            .Produces<IEnumerable<TaskActivityDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListAsync(
        Guid taskId,
        ClaimsPrincipal principal,
        AppDbContext db,
        ProjectAccessGuard access,
        CancellationToken ct)
    {
        var task = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null || !await access.HasAccessAsync(principal, task.ProjectId, ct))
            return Results.NotFound();

        var activity = await db.TaskActivities.AsNoTracking()
            .Include(a => a.User)
            .Where(a => a.TaskId == taskId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

        return Results.Ok(activity.Select(a => new TaskActivityDto(a.Id, a.UserId, a.User.FullName, a.Action, a.PayloadJson, a.CreatedAt)));
    }
}
