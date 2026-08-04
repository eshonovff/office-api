using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Permissions = Office.Api.Auth.Permissions;

namespace Office.Api.Features.Roles;

public static class RolesEndpoints
{
    public static IEndpointRouteBuilder MapRolesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/roles").WithTags("Roles");

        group.MapGet("/", ListAsync).RequirePermission(Permissions.Roles.View);

        group.MapPost("/", CreateAsync)
            .WithValidation<CreateRoleRequest>()
            .RequirePermission(Permissions.Roles.Manage);

        group.MapPatch("/{id:guid}", UpdateAsync)
            .WithValidation<UpdateRoleRequest>()
            .RequirePermission(Permissions.Roles.Manage);

        group.MapPut("/{id:guid}/permissions", SetPermissionsAsync)
            .WithValidation<SetRolePermissionsRequest>()
            .RequirePermission(Permissions.Roles.Manage);

        group.MapDelete("/{id:guid}", DeleteAsync).RequirePermission(Permissions.Roles.Manage);

        app.MapGet("/api/permissions", ListPermissionsAsync)
            .WithTags("Roles")
            .RequirePermission(Permissions.Roles.View);

        return app;
    }

    private static async Task<IResult> ListAsync(AppDbContext db, CancellationToken ct)
    {
        var roles = await db.Roles.AsNoTracking()
            .Include(r => r.RolePermissions)
            .OrderBy(r => r.Name)
            .ToListAsync(ct);

        return Results.Ok(roles.Select(ToListItem));
    }

    private static IResult ListPermissionsAsync()
        => Results.Ok(Permissions.All.OrderBy(p => p, StringComparer.Ordinal));

    private static async Task<IResult> CreateAsync(CreateRoleRequest request, AppDbContext db, CancellationToken ct)
    {
        var keyTaken = await db.Roles.AnyAsync(r => r.Key == request.Key, ct);
        if (keyTaken)
        {
            return Results.Problem(
                title: "Калиди роль банд аст",
                detail: "Ин калиди роль аллакай истифода мешавад.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var role = new Role
        {
            Id = Guid.CreateVersion7(),
            Key = request.Key,
            Name = request.Name,
            Description = request.Description,
            IsSystem = false,
        };

        db.Roles.Add(role);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/roles/{role.Id}", ToListItem(role));
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateRoleRequest request, AppDbContext db, CancellationToken ct)
    {
        var role = await db.Roles.Include(r => r.RolePermissions).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null)
            return Results.NotFound();

        role.Name = request.Name;
        role.Description = request.Description;

        await db.SaveChangesAsync(ct);
        return Results.Ok(ToListItem(role));
    }

    private static async Task<IResult> SetPermissionsAsync(
        Guid id,
        SetRolePermissionsRequest request,
        AppDbContext db,
        IPermissionService permissionService,
        CancellationToken ct)
    {
        var role = await db.Roles.Include(r => r.RolePermissions).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null)
            return Results.NotFound();

        role.RolePermissions.Clear();
        foreach (var permissionKey in request.PermissionKeys.Distinct())
            role.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionKey = permissionKey });

        var affectedUserIds = await db.UserRoles
            .Where(ur => ur.RoleId == id)
            .Select(ur => ur.UserId)
            .ToListAsync(ct);

        await db.SaveChangesAsync(ct);

        foreach (var userId in affectedUserIds)
            await permissionService.BumpVersionAsync(userId, ct);

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null)
            return Results.NotFound();

        if (role.IsSystem)
        {
            return Results.Problem(
                title: "Роли системавӣ",
                detail: "Роли системавиро нест кардан мумкин нест.",
                statusCode: StatusCodes.Status409Conflict);
        }

        db.Roles.Remove(role);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static RoleListItem ToListItem(Role role) => new(
        role.Id,
        role.Key,
        role.Name,
        role.Description,
        role.IsSystem,
        role.RolePermissions.Select(rp => rp.PermissionKey).ToList());
}
