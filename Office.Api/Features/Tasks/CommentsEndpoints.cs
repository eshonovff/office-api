using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Realtime;
using Permissions = Office.Api.Auth.Permissions;

namespace Office.Api.Features.Tasks;

public static class CommentsEndpoints
{
    public static IEndpointRouteBuilder MapCommentsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tasks/{taskId:guid}/comments").WithTags("Tasks");

        group.MapGet("/", ListAsync).RequirePermission(Permissions.Tasks.View);

        group.MapPost("/", CreateAsync)
            .WithValidation<CreateCommentRequest>()
            .RequirePermission(Permissions.Tasks.View);

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

        var comments = await db.TaskComments.AsNoTracking()
            .Include(c => c.Author)
            .Where(c => c.TaskId == taskId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

        return Results.Ok(comments.Select(c => new TaskCommentDto(c.Id, c.AuthorId, c.Author.FullName, c.Body, c.CreatedAt)));
    }

    private static async Task<IResult> CreateAsync(
        Guid taskId,
        CreateCommentRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        ProjectAccessGuard access,
        IBoardEventPublisher publisher,
        INotificationService notifications,
        CancellationToken ct)
    {
        var task = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null || !await access.HasAccessAsync(principal, task.ProjectId, ct))
            return Results.NotFound();

        var userId = principal.GetUserId();
        var comment = new TaskComment
        {
            Id = Guid.CreateVersion7(),
            TaskId = taskId,
            AuthorId = userId,
            Body = request.Body,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.TaskComments.Add(comment);

        var mentionedUsernames = MentionParser.ExtractUsernames(request.Body);
        var mentionedUserIds = new List<Guid>();
        if (mentionedUsernames.Count > 0)
        {
            mentionedUserIds = await db.ProjectMembers
                .Where(pm => pm.ProjectId == task.ProjectId && mentionedUsernames.Contains(pm.User.Username))
                .Select(pm => pm.UserId)
                .ToListAsync(ct);

            if (mentionedUserIds.Count > 0)
            {
                var payload = JsonSerializer.Serialize(new { commentId = comment.Id, mentionedUserIds });
                db.TaskActivities.Add(new TaskActivity
                {
                    Id = Guid.CreateVersion7(),
                    TaskId = taskId,
                    UserId = userId,
                    Action = "mention",
                    PayloadJson = payload,
                    CreatedAt = DateTimeOffset.UtcNow,
                });
            }
        }

        db.TaskActivities.Add(new TaskActivity
        {
            Id = Guid.CreateVersion7(),
            TaskId = taskId,
            UserId = userId,
            Action = "commented",
            PayloadJson = JsonSerializer.Serialize(new { commentId = comment.Id }),
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(ct);

        var author = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId, ct);
        var dto = new TaskCommentDto(comment.Id, userId, author.FullName, comment.Body, comment.CreatedAt);

        await publisher.CommentAddedAsync(task.ProjectId, dto, ct);

        foreach (var mentionedUserId in mentionedUserIds.Where(id => id != userId))
        {
            await notifications.PushAsync(
                mentionedUserId, "mention", new { taskId, commentId = comment.Id }, ct);
        }

        return Results.Created($"/api/tasks/{taskId}/comments/{comment.Id}", dto);
    }
}
