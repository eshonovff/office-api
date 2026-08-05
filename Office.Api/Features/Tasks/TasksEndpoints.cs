using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Features.Projects;
using Permissions = Office.Api.Auth.Permissions;
using TaskEntity = Office.Api.Data.Entities.TaskItem;

namespace Office.Api.Features.Tasks;

public static class TasksEndpoints
{
    public static IEndpointRouteBuilder MapTasksEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/projects/{projectId:guid}/board", BoardAsync)
            .WithTags("Tasks")
            .RequirePermission(Permissions.Tasks.View)
            .WithSummary("Board — ҳамаи колонкаҳо бо таскҳояшон, як query")
            .Produces<BoardResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        var group = app.MapGroup("/api/tasks").WithTags("Tasks");

        group.MapGet("/", ListAsync)
            .RequirePermission(Permissions.Tasks.View)
            .WithSummary("Рӯйхати таск — филтр бо масъул, тег, приоритет, deadline, матн")
            .Produces<IEnumerable<TaskListItem>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", GetAsync)
            .RequirePermission(Permissions.Tasks.View)
            .WithSummary("Маълумоти пурраи таск")
            .Produces<TaskDetail>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithValidation<CreateTaskRequest>()
            .RequirePermission(Permissions.Tasks.Create)
            .WithSummary("Сохтани таски нав — position = охирини колонка + 1000")
            .Produces<TaskDetail>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/{id:guid}", UpdateAsync)
            .WithValidation<UpdateTaskRequest>()
            .RequirePermission(Permissions.Tasks.Edit)
            .WithSummary("Навсозии сарлавҳа, тавсиф, приоритет, deadline, тегҳо")
            .Produces<TaskDetail>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .RequirePermission(Permissions.Tasks.Delete)
            .WithSummary("Нест кардани таск")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/{id:guid}/move", MoveAsync)
            .WithValidation<MoveTaskRequest>()
            .RequirePermission(Permissions.Tasks.Move)
            .WithSummary("Кӯчонидан (drag & drop) — columnId, beforeTaskId?, afterTaskId?")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/{id:guid}/assign", AssignAsync)
            .RequirePermission(Permissions.Tasks.Assign)
            .WithSummary("Таъин кардани масъул (ё бекор кардани таъин бо null)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> BoardAsync(
        Guid projectId,
        ClaimsPrincipal principal,
        AppDbContext db,
        ProjectAccessGuard access,
        CancellationToken ct)
    {
        if (!await access.HasAccessAsync(principal, projectId, ct))
            return Results.NotFound();

        var columns = await db.BoardColumns.AsNoTracking()
            .Where(c => c.ProjectId == projectId)
            .OrderBy(c => c.OrderIndex)
            .Include(c => c.Tasks).ThenInclude(t => t.Assignee)
            .Include(c => c.Tasks).ThenInclude(t => t.TaskLabels).ThenInclude(tl => tl.Label)
            .ToListAsync(ct);

        var result = columns.Select(c => new BoardColumnWithTasks(
            c.Id,
            c.Name,
            c.OrderIndex,
            c.IsDoneColumn,
            c.Tasks.OrderBy(t => t.Position).Select(ToListItem).ToList()));

        return Results.Ok(new BoardResponse(result.ToList()));
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal principal,
        AppDbContext db,
        Guid? assigneeId,
        Guid? labelId,
        string? priority,
        DateTimeOffset? dueFrom,
        DateTimeOffset? dueTo,
        string? search,
        CancellationToken ct)
    {
        var query = db.Tasks.AsNoTracking()
            .Include(t => t.Assignee)
            .Include(t => t.TaskLabels).ThenInclude(tl => tl.Label)
            .AsQueryable();

        if (!ProjectAccessGuard.CanSeeAllProjects(principal))
        {
            var userId = principal.GetUserId();
            query = query.Where(t => t.Project.Members.Any(m => m.UserId == userId));
        }

        if (assigneeId is not null)
            query = query.Where(t => t.AssigneeId == assigneeId);

        if (labelId is not null)
            query = query.Where(t => t.TaskLabels.Any(tl => tl.LabelId == labelId));

        if (!string.IsNullOrWhiteSpace(priority) && Enum.TryParse<TaskPriority>(priority, ignoreCase: true, out var parsedPriority))
            query = query.Where(t => t.Priority == parsedPriority);

        if (dueFrom is not null)
            query = query.Where(t => t.DueDate >= dueFrom);

        if (dueTo is not null)
            query = query.Where(t => t.DueDate <= dueTo);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(t => EF.Functions.ILike(t.Title, pattern) ||
                                      (t.Description != null && EF.Functions.ILike(t.Description, pattern)));
        }

        var tasks = await query.OrderByDescending(t => t.CreatedAt).ToListAsync(ct);
        return Results.Ok(tasks.Select(ToListItem));
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        ClaimsPrincipal principal,
        AppDbContext db,
        ProjectAccessGuard access,
        CancellationToken ct)
    {
        var task = await db.Tasks.AsNoTracking()
            .Include(t => t.Assignee)
            .Include(t => t.TaskLabels).ThenInclude(tl => tl.Label)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (task is null || !await access.HasAccessAsync(principal, task.ProjectId, ct))
            return Results.NotFound();

        return Results.Ok(ToDetail(task));
    }

    private static async Task<IResult> CreateAsync(
        CreateTaskRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        ProjectAccessGuard access,
        CancellationToken ct)
    {
        if (!await access.HasAccessAsync(principal, request.ProjectId, ct))
            return Results.NotFound();

        var column = await db.BoardColumns.FirstOrDefaultAsync(c => c.Id == request.ColumnId && c.ProjectId == request.ProjectId, ct);
        if (column is null)
            return Results.NotFound();

        if (request.AssigneeId is not null && !await IsProjectMemberAsync(db, request.ProjectId, request.AssigneeId.Value, ct))
        {
            return Results.Problem(
                title: "Масъул узви проект нест",
                detail: "Масъул бояд узви проект бошад.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var lastPosition = await db.Tasks
            .Where(t => t.ColumnId == request.ColumnId)
            .OrderByDescending(t => t.Position)
            .Select(t => (double?)t.Position)
            .FirstOrDefaultAsync(ct);

        var userId = principal.GetUserId();
        var now = DateTimeOffset.UtcNow;

        var task = new TaskEntity
        {
            Id = Guid.CreateVersion7(),
            ProjectId = request.ProjectId,
            ColumnId = request.ColumnId,
            Title = request.Title,
            Description = request.Description,
            AssigneeId = request.AssigneeId,
            Priority = Enum.Parse<TaskPriority>(request.Priority, ignoreCase: true),
            DueDate = request.DueDate,
            Position = (lastPosition ?? 0) + PositionCalculator.Step,
            CreatedBy = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        if (request.LabelIds is { Count: > 0 })
            await AttachLabelsAsync(db, task, request.ProjectId, request.LabelIds, ct);

        db.Tasks.Add(task);
        db.TaskActivities.Add(NewActivity(task.Id, userId, "created", null));

        await db.SaveChangesAsync(ct);

        var created = await db.Tasks.AsNoTracking()
            .Include(t => t.Assignee)
            .Include(t => t.TaskLabels).ThenInclude(tl => tl.Label)
            .FirstAsync(t => t.Id == task.Id, ct);

        return Results.Created($"/api/tasks/{task.Id}", ToDetail(created));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateTaskRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        ProjectAccessGuard access,
        CancellationToken ct)
    {
        var task = await db.Tasks.Include(t => t.TaskLabels).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null || !await access.HasAccessAsync(principal, task.ProjectId, ct))
            return Results.NotFound();

        task.Title = request.Title;
        task.Description = request.Description;
        task.Priority = Enum.Parse<TaskPriority>(request.Priority, ignoreCase: true);
        task.DueDate = request.DueDate;
        task.UpdatedAt = DateTimeOffset.UtcNow;

        if (request.LabelIds is not null)
        {
            task.TaskLabels.Clear();
            if (request.LabelIds.Count > 0)
                await AttachLabelsAsync(db, task, task.ProjectId, request.LabelIds, ct);
        }

        db.TaskActivities.Add(NewActivity(task.Id, principal.GetUserId(), "updated", null));

        await db.SaveChangesAsync(ct);

        var updated = await db.Tasks.AsNoTracking()
            .Include(t => t.Assignee)
            .Include(t => t.TaskLabels).ThenInclude(tl => tl.Label)
            .FirstAsync(t => t.Id == id, ct);

        return Results.Ok(ToDetail(updated));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        ClaimsPrincipal principal,
        AppDbContext db,
        ProjectAccessGuard access,
        CancellationToken ct)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null || !await access.HasAccessAsync(principal, task.ProjectId, ct))
            return Results.NotFound();

        db.Tasks.Remove(task);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static async Task<IResult> MoveAsync(
        Guid id,
        MoveTaskRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        ProjectAccessGuard access,
        CancellationToken ct)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null || !await access.HasAccessAsync(principal, task.ProjectId, ct))
            return Results.NotFound();

        var targetColumn = await db.BoardColumns.FirstOrDefaultAsync(c => c.Id == request.ColumnId && c.ProjectId == task.ProjectId, ct);
        if (targetColumn is null)
            return Results.NotFound();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Advisory lock барои колонкаи ҳадаф — то ду кӯчонидани ҳамзамон ба ҳамин колонка вайрон накунад.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({request.ColumnId.ToString()}))", ct);

        var siblings = await db.Tasks
            .Where(t => t.ColumnId == request.ColumnId && t.Id != id)
            .OrderBy(t => t.Position)
            .ToListAsync(ct);

        double? beforePosition = null;
        double? afterPosition = null;

        if (request.BeforeTaskId is not null)
        {
            var before = siblings.FirstOrDefault(t => t.Id == request.BeforeTaskId);
            if (before is null)
                return Results.BadRequest();
            beforePosition = before.Position;
        }

        if (request.AfterTaskId is not null)
        {
            var after = siblings.FirstOrDefault(t => t.Id == request.AfterTaskId);
            if (after is null)
                return Results.BadRequest();
            afterPosition = after.Position;
        }

        var newPosition = PositionCalculator.Calculate(beforePosition, afterPosition);

        if (PositionCalculator.NeedsReindex(beforePosition, afterPosition, newPosition))
        {
            var ordered = new List<TaskEntity>(siblings);
            var insertIndex = request.BeforeTaskId is null
                ? 0
                : ordered.FindIndex(t => t.Id == request.BeforeTaskId) + 1;
            ordered.Insert(insertIndex, task);

            var position = PositionCalculator.Step;
            foreach (var sibling in ordered)
            {
                sibling.Position = position;
                position += PositionCalculator.Step;
            }
        }
        else
        {
            task.Position = newPosition;
        }

        task.ColumnId = request.ColumnId;
        task.UpdatedAt = DateTimeOffset.UtcNow;

        db.TaskActivities.Add(NewActivity(task.Id, principal.GetUserId(), "moved", $$"""{"columnId":"{{request.ColumnId}}"}"""));

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return Results.NoContent();
    }

    private static async Task<IResult> AssignAsync(
        Guid id,
        AssignTaskRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        ProjectAccessGuard access,
        CancellationToken ct)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null || !await access.HasAccessAsync(principal, task.ProjectId, ct))
            return Results.NotFound();

        if (request.AssigneeId is not null && !await IsProjectMemberAsync(db, task.ProjectId, request.AssigneeId.Value, ct))
        {
            return Results.Problem(
                title: "Масъул узви проект нест",
                detail: "Масъул бояд узви проект бошад.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        task.AssigneeId = request.AssigneeId;
        task.UpdatedAt = DateTimeOffset.UtcNow;

        db.TaskActivities.Add(NewActivity(task.Id, principal.GetUserId(), "assigned", $$"""{"assigneeId":{{(request.AssigneeId is null ? "null" : $"\"{request.AssigneeId}\"")}}}"""));

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<bool> IsProjectMemberAsync(AppDbContext db, Guid projectId, Guid userId, CancellationToken ct)
        => await db.ProjectMembers.AnyAsync(pm => pm.ProjectId == projectId && pm.UserId == userId, ct);

    private static async Task AttachLabelsAsync(AppDbContext db, TaskEntity task, Guid projectId, IReadOnlyList<Guid> labelIds, CancellationToken ct)
    {
        var distinctIds = labelIds.Distinct().ToList();
        var validCount = await db.Labels.CountAsync(l => l.ProjectId == projectId && distinctIds.Contains(l.Id), ct);
        if (validCount != distinctIds.Count)
            throw new InvalidOperationException("Яке аз тегҳо ба проект тааллуқ надорад.");

        foreach (var labelId in distinctIds)
            task.TaskLabels.Add(new TaskLabel { TaskId = task.Id, LabelId = labelId });
    }

    private static TaskActivity NewActivity(Guid taskId, Guid userId, string action, string? payloadJson) => new()
    {
        Id = Guid.CreateVersion7(),
        TaskId = taskId,
        UserId = userId,
        Action = action,
        PayloadJson = payloadJson,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static TaskListItem ToListItem(TaskEntity task) => new(
        task.Id,
        task.ProjectId,
        task.ColumnId,
        task.Title,
        task.AssigneeId,
        task.Assignee?.FullName,
        task.Priority.ToString(),
        task.DueDate,
        task.Position,
        task.TaskLabels.Select(tl => new LabelDto(tl.Label.Id, tl.Label.Name, tl.Label.Color)).ToList());

    private static TaskDetail ToDetail(TaskEntity task) => new(
        task.Id,
        task.ProjectId,
        task.ColumnId,
        task.Title,
        task.Description,
        task.AssigneeId,
        task.Assignee?.FullName,
        task.Priority.ToString(),
        task.DueDate,
        task.Position,
        task.CreatedBy,
        task.CreatedAt,
        task.UpdatedAt,
        task.TaskLabels.Select(tl => new LabelDto(tl.Label.Id, tl.Label.Name, tl.Label.Color)).ToList());
}
