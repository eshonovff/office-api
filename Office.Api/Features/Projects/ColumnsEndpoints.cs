using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Permissions = Office.Api.Auth.Permissions;

namespace Office.Api.Features.Projects;

public static class ColumnsEndpoints
{
    public static IEndpointRouteBuilder MapColumnsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/projects/{projectId:guid}/columns").WithTags("Columns");

        group.MapPost("/", CreateAsync)
            .WithValidation<CreateColumnRequest>()
            .RequirePermission(Permissions.Projects.Manage)
            .WithSummary("Илова кардани колонкаи нав дар охири board")
            .Produces<BoardColumnDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/{columnId:guid}", UpdateAsync)
            .WithValidation<UpdateColumnRequest>()
            .RequirePermission(Permissions.Projects.Manage)
            .WithSummary("Навсозии ном ё is_done_column-и колонка")
            .Produces<BoardColumnDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{columnId:guid}", DeleteAsync)
            .RequirePermission(Permissions.Projects.Manage)
            .WithSummary("Нест кардани колонка (танҳо агар таск надошта бошад)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/order", ReorderAsync)
            .WithValidation<ReorderColumnsRequest>()
            .RequirePermission(Permissions.Projects.Manage)
            .WithSummary("Тағйири тартиби колонкаҳои board")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        Guid projectId,
        CreateColumnRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        ProjectAccessGuard access,
        CancellationToken ct)
    {
        if (!await access.HasAccessAsync(principal, projectId, ct))
            return Results.NotFound();

        var projectExists = await db.Projects.AnyAsync(p => p.Id == projectId, ct);
        if (!projectExists)
            return Results.NotFound();

        var maxOrder = await db.BoardColumns
            .Where(c => c.ProjectId == projectId)
            .Select(c => (int?)c.OrderIndex)
            .MaxAsync(ct) ?? -1;

        var column = new BoardColumn
        {
            Id = Guid.CreateVersion7(),
            ProjectId = projectId,
            Name = request.Name,
            OrderIndex = maxOrder + 1,
            IsDoneColumn = request.IsDoneColumn,
        };

        db.BoardColumns.Add(column);
        await db.SaveChangesAsync(ct);

        return Results.Created(
            $"/api/projects/{projectId}/columns/{column.Id}",
            new BoardColumnDto(column.Id, column.Name, column.OrderIndex, column.IsDoneColumn));
    }

    private static async Task<IResult> UpdateAsync(
        Guid projectId,
        Guid columnId,
        UpdateColumnRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        ProjectAccessGuard access,
        CancellationToken ct)
    {
        if (!await access.HasAccessAsync(principal, projectId, ct))
            return Results.NotFound();

        var column = await db.BoardColumns.FirstOrDefaultAsync(c => c.Id == columnId && c.ProjectId == projectId, ct);
        if (column is null)
            return Results.NotFound();

        column.Name = request.Name;
        column.IsDoneColumn = request.IsDoneColumn;

        await db.SaveChangesAsync(ct);
        return Results.Ok(new BoardColumnDto(column.Id, column.Name, column.OrderIndex, column.IsDoneColumn));
    }

    private static async Task<IResult> DeleteAsync(
        Guid projectId,
        Guid columnId,
        ClaimsPrincipal principal,
        AppDbContext db,
        ProjectAccessGuard access,
        CancellationToken ct)
    {
        if (!await access.HasAccessAsync(principal, projectId, ct))
            return Results.NotFound();

        var column = await db.BoardColumns.FirstOrDefaultAsync(c => c.Id == columnId && c.ProjectId == projectId, ct);
        if (column is null)
            return Results.NotFound();

        var hasTasks = await db.Tasks.AnyAsync(t => t.ColumnId == columnId, ct);
        if (hasTasks)
        {
            return Results.Problem(
                title: "Колонка холӣ нест",
                detail: "Колонкаи дорои таск нест кардан мумкин нест.",
                statusCode: StatusCodes.Status409Conflict);
        }

        db.BoardColumns.Remove(column);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static async Task<IResult> ReorderAsync(
        Guid projectId,
        ReorderColumnsRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        ProjectAccessGuard access,
        CancellationToken ct)
    {
        if (!await access.HasAccessAsync(principal, projectId, ct))
            return Results.NotFound();

        var columns = await db.BoardColumns.Where(c => c.ProjectId == projectId).ToListAsync(ct);
        var columnIds = request.ColumnIds.Distinct().ToList();

        if (columnIds.Count != columns.Count || columns.Any(c => !columnIds.Contains(c.Id)))
        {
            return Results.Problem(
                title: "Рӯйхати колонка нодуруст",
                detail: "Рӯйхати колонка бояд ҳамаи колонкаҳои проектро дар бар гирад.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        for (var i = 0; i < columnIds.Count; i++)
            columns.First(c => c.Id == columnIds[i]).OrderIndex = i;

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
