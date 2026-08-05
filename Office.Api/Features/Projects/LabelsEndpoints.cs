using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Permissions = Office.Api.Auth.Permissions;

namespace Office.Api.Features.Projects;

public static class LabelsEndpoints
{
    public static IEndpointRouteBuilder MapLabelsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/projects/{projectId:guid}/labels").WithTags("Projects");

        group.MapGet("/", ListAsync).RequirePermission(Permissions.Projects.View);

        group.MapPost("/", CreateAsync)
            .WithValidation<CreateLabelRequest>()
            .RequirePermission(Permissions.Projects.Manage);

        group.MapPatch("/{labelId:guid}", UpdateAsync)
            .WithValidation<UpdateLabelRequest>()
            .RequirePermission(Permissions.Projects.Manage);

        group.MapDelete("/{labelId:guid}", DeleteAsync).RequirePermission(Permissions.Projects.Manage);

        return app;
    }

    private static async Task<IResult> ListAsync(
        Guid projectId,
        ClaimsPrincipal principal,
        AppDbContext db,
        ProjectAccessGuard access,
        CancellationToken ct)
    {
        if (!await access.HasAccessAsync(principal, projectId, ct))
            return Results.NotFound();

        var labels = await db.Labels.AsNoTracking()
            .Where(l => l.ProjectId == projectId)
            .OrderBy(l => l.Name)
            .ToListAsync(ct);

        return Results.Ok(labels.Select(l => new LabelDto(l.Id, l.Name, l.Color)));
    }

    private static async Task<IResult> CreateAsync(
        Guid projectId,
        CreateLabelRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        ProjectAccessGuard access,
        CancellationToken ct)
    {
        if (!await access.HasAccessAsync(principal, projectId, ct))
            return Results.NotFound();

        var nameTaken = await db.Labels.AnyAsync(l => l.ProjectId == projectId && l.Name == request.Name, ct);
        if (nameTaken)
        {
            return Results.Problem(
                title: "Номи тег банд аст",
                detail: "Ин ном дар проект аллакай истифода мешавад.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var label = new Label
        {
            Id = Guid.CreateVersion7(),
            ProjectId = projectId,
            Name = request.Name,
            Color = request.Color,
        };

        db.Labels.Add(label);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/projects/{projectId}/labels/{label.Id}", new LabelDto(label.Id, label.Name, label.Color));
    }

    private static async Task<IResult> UpdateAsync(
        Guid projectId,
        Guid labelId,
        UpdateLabelRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        ProjectAccessGuard access,
        CancellationToken ct)
    {
        if (!await access.HasAccessAsync(principal, projectId, ct))
            return Results.NotFound();

        var label = await db.Labels.FirstOrDefaultAsync(l => l.Id == labelId && l.ProjectId == projectId, ct);
        if (label is null)
            return Results.NotFound();

        label.Name = request.Name;
        label.Color = request.Color;

        await db.SaveChangesAsync(ct);
        return Results.Ok(new LabelDto(label.Id, label.Name, label.Color));
    }

    private static async Task<IResult> DeleteAsync(
        Guid projectId,
        Guid labelId,
        ClaimsPrincipal principal,
        AppDbContext db,
        ProjectAccessGuard access,
        CancellationToken ct)
    {
        if (!await access.HasAccessAsync(principal, projectId, ct))
            return Results.NotFound();

        var label = await db.Labels.FirstOrDefaultAsync(l => l.Id == labelId && l.ProjectId == projectId, ct);
        if (label is null)
            return Results.NotFound();

        db.Labels.Remove(label);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
