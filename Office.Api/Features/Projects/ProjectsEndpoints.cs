using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Permissions = Office.Api.Auth.Permissions;

namespace Office.Api.Features.Projects;

public static class ProjectsEndpoints
{
    private static readonly (string Name, bool IsDoneColumn)[] DefaultColumns =
    [
        ("Дар навбат", false),
        ("Дар кор", false),
        ("Тафтиш", false),
        ("Тамом", true),
    ];

    public static IEndpointRouteBuilder MapProjectsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/projects").WithTags("Projects");

        group.MapGet("/", ListAsync)
            .RequirePermission(Permissions.Projects.View)
            .WithSummary("Рӯйхати проектҳо — танҳо проектҳое, ки узв ҳастӣ (Owner/Admin ҳама)")
            .Produces<IEnumerable<ProjectListItem>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", GetAsync)
            .RequirePermission(Permissions.Projects.View)
            .WithSummary("Маълумоти пурраи проект — аъзо ва колонкаҳо")
            .Produces<ProjectDetail>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithValidation<CreateProjectRequest>()
            .RequirePermission(Permissions.Projects.Manage)
            .WithSummary("Сохтани проекти нав бо 4 колонкаи пешфарз")
            .Produces<ProjectListItem>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/{id:guid}", UpdateAsync)
            .WithValidation<UpdateProjectRequest>()
            .RequirePermission(Permissions.Projects.Manage)
            .WithSummary("Навсозии ном ва ранги проект")
            .Produces<ProjectListItem>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/archive", ArchiveAsync)
            .RequirePermission(Permissions.Projects.Manage)
            .WithSummary("Бойгонӣ кардани проект")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}/members", SetMembersAsync)
            .WithValidation<SetProjectMembersRequest>()
            .RequirePermission(Permissions.Projects.Manage)
            .WithSummary("Танзими рӯйхати аъзои проект")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListAsync(ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var query = db.Projects.AsNoTracking().AsQueryable();

        if (!ProjectAccessGuard.CanSeeAllProjects(principal))
        {
            var userId = principal.GetUserId();
            query = query.Where(p => p.Members.Any(m => m.UserId == userId));
        }

        var projects = await query.OrderBy(p => p.Name).ToListAsync(ct);
        return Results.Ok(projects.Select(ToListItem));
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        ClaimsPrincipal principal,
        AppDbContext db,
        ProjectAccessGuard access,
        CancellationToken ct)
    {
        if (!await access.HasAccessAsync(principal, id, ct))
            return Results.NotFound();

        var project = await db.Projects.AsNoTracking()
            .Include(p => p.Members).ThenInclude(m => m.User)
            .Include(p => p.Columns)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        return project is null ? Results.NotFound() : Results.Ok(ToDetail(project));
    }

    private static async Task<IResult> CreateAsync(CreateProjectRequest request, AppDbContext db, CancellationToken ct)
    {
        var keyTaken = await db.Projects.AnyAsync(p => p.Key == request.Key, ct);
        if (keyTaken)
        {
            return Results.Problem(
                title: "Калиди проект банд аст",
                detail: "Ин калиди проект аллакай истифода мешавад.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var project = new Project
        {
            Id = Guid.CreateVersion7(),
            Name = request.Name,
            Key = request.Key,
            Color = request.Color,
            IsArchived = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var orderIndex = 0;
        foreach (var (name, isDoneColumn) in DefaultColumns)
        {
            project.Columns.Add(new BoardColumn
            {
                Id = Guid.CreateVersion7(),
                ProjectId = project.Id,
                Name = name,
                OrderIndex = orderIndex++,
                IsDoneColumn = isDoneColumn,
            });
        }

        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/projects/{project.Id}", ToListItem(project));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateProjectRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        ProjectAccessGuard access,
        CancellationToken ct)
    {
        if (!await access.HasAccessAsync(principal, id, ct))
            return Results.NotFound();

        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null)
            return Results.NotFound();

        project.Name = request.Name;
        project.Color = request.Color;

        await db.SaveChangesAsync(ct);
        return Results.Ok(ToListItem(project));
    }

    private static async Task<IResult> ArchiveAsync(
        Guid id,
        ClaimsPrincipal principal,
        AppDbContext db,
        ProjectAccessGuard access,
        CancellationToken ct)
    {
        if (!await access.HasAccessAsync(principal, id, ct))
            return Results.NotFound();

        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null)
            return Results.NotFound();

        project.IsArchived = true;
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static async Task<IResult> SetMembersAsync(
        Guid id,
        SetProjectMembersRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        ProjectAccessGuard access,
        CancellationToken ct)
    {
        if (!await access.HasAccessAsync(principal, id, ct))
            return Results.NotFound();

        var project = await db.Projects.Include(p => p.Members).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null)
            return Results.NotFound();

        var userIds = request.UserIds.Distinct().ToList();
        var existingUserCount = await db.Users.CountAsync(u => userIds.Contains(u.Id), ct);
        if (existingUserCount != userIds.Count)
        {
            return Results.Problem(
                title: "Корбари нодуруст",
                detail: "Яке аз корбарон вуҷуд надорад.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        project.Members.Clear();
        foreach (var userId in userIds)
            project.Members.Add(new ProjectMember { ProjectId = project.Id, UserId = userId });

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static ProjectListItem ToListItem(Project project) => new(
        project.Id, project.Name, project.Key, project.Color, project.IsArchived);

    private static ProjectDetail ToDetail(Project project) => new(
        project.Id,
        project.Name,
        project.Key,
        project.Color,
        project.IsArchived,
        project.CreatedAt,
        project.Members.Select(m => new ProjectMemberDto(m.UserId, m.User.FullName, m.User.Username)).ToList(),
        project.Columns.OrderBy(c => c.OrderIndex).Select(c => new BoardColumnDto(c.Id, c.Name, c.OrderIndex, c.IsDoneColumn)).ToList());
}
