using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Permissions = Office.Api.Auth.Permissions;

namespace Office.Api.Features.Users;

public static class UsersEndpoints
{
    public static IEndpointRouteBuilder MapUsersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").WithTags("Users");

        group.MapGet("/", ListAsync).RequirePermission(Permissions.Users.View);
        group.MapGet("/{id:guid}", GetAsync).RequirePermission(Permissions.Users.View);

        group.MapPost("/", CreateAsync)
            .WithValidation<CreateUserRequest>()
            .RequirePermission(Permissions.Users.Manage);

        group.MapPatch("/{id:guid}", UpdateAsync)
            .WithValidation<UpdateUserRequest>()
            .RequirePermission(Permissions.Users.Manage);

        group.MapPut("/{id:guid}/roles", SetRolesAsync)
            .WithValidation<SetUserRolesRequest>()
            .RequirePermission(Permissions.Users.Manage);

        group.MapPut("/{id:guid}/permissions", SetPermissionsAsync)
            .WithValidation<SetUserPermissionsRequest>()
            .RequirePermission(Permissions.Users.Manage);

        group.MapPost("/{id:guid}/reset-password", ResetPasswordAsync)
            .RequirePermission(Permissions.Users.Manage);

        group.MapPatch("/{id:guid}/active", SetActiveAsync)
            .RequirePermission(Permissions.Users.Manage);

        return app;
    }

    private static async Task<IResult> ListAsync(
        AppDbContext db,
        string? search,
        Guid? roleId,
        bool? isActive,
        CancellationToken ct)
    {
        var query = db.Users.AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(u => EF.Functions.ILike(u.FullName, pattern) || EF.Functions.ILike(u.Username, pattern));
        }

        if (roleId is not null)
            query = query.Where(u => u.UserRoles.Any(ur => ur.RoleId == roleId));

        if (isActive is not null)
            query = query.Where(u => u.IsActive == isActive);

        var users = await query.OrderBy(u => u.FullName).ToListAsync(ct);
        return Results.Ok(users.Select(ToListItem));
    }

    private static async Task<IResult> GetAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.UserPermissions)
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        return user is null ? Results.NotFound() : Results.Ok(ToDetail(user));
    }

    private static async Task<IResult> CreateAsync(CreateUserRequest request, AppDbContext db, CancellationToken ct)
    {
        var usernameTaken = await db.Users.AnyAsync(u => u.Username == request.Username, ct);
        if (usernameTaken)
        {
            return Results.Problem(
                title: "Логин банд аст",
                detail: "Ин логин аллакай истифода мешавад.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var temporaryPassword = PasswordGenerator.Generate();

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            FullName = request.FullName,
            Username = request.Username,
            Phone = request.Phone,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(temporaryPassword),
            IsActive = true,
            MustChangePassword = true,
            PermissionsVersion = 1,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/users/{user.Id}", new CreateUserResponse(user.Id, user.Username, temporaryPassword));
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateUserRequest request, AppDbContext db, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return Results.NotFound();

        user.FullName = request.FullName;
        user.Phone = request.Phone;
        user.OnlyAssigned = request.OnlyAssigned;

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SetRolesAsync(
        Guid id,
        SetUserRolesRequest request,
        AppDbContext db,
        IPermissionService permissionService,
        CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.UserRoles).FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return Results.NotFound();

        var roleIds = request.RoleIds.Distinct().ToList();
        var existingRoleCount = await db.Roles.CountAsync(r => roleIds.Contains(r.Id), ct);
        if (existingRoleCount != roleIds.Count)
        {
            return Results.Problem(
                title: "Роли нодуруст",
                detail: "Яке аз роль-ҳо вуҷуд надорад.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        user.UserRoles.Clear();
        foreach (var roleId in roleIds)
            user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roleId });

        await db.SaveChangesAsync(ct);
        await permissionService.BumpVersionAsync(user.Id, ct);

        return Results.NoContent();
    }

    private static async Task<IResult> SetPermissionsAsync(
        Guid id,
        SetUserPermissionsRequest request,
        AppDbContext db,
        IPermissionService permissionService,
        CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.UserPermissions).FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return Results.NotFound();

        user.UserPermissions.Clear();
        foreach (var exception in request.Exceptions)
        {
            user.UserPermissions.Add(new UserPermission
            {
                UserId = user.Id,
                PermissionKey = exception.PermissionKey,
                IsGranted = exception.IsGranted,
            });
        }

        await db.SaveChangesAsync(ct);
        await permissionService.BumpVersionAsync(user.Id, ct);

        return Results.NoContent();
    }

    private static async Task<IResult> ResetPasswordAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return Results.NotFound();

        var temporaryPassword = PasswordGenerator.Generate();
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(temporaryPassword);
        user.MustChangePassword = true;

        await db.SaveChangesAsync(ct);
        return Results.Ok(new ResetPasswordResponse(temporaryPassword));
    }

    private static async Task<IResult> SetActiveAsync(Guid id, SetActiveRequest request, AppDbContext db, CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role).FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return Results.NotFound();

        if (!request.IsActive && user.UserRoles.Any(ur => ur.Role.Key == RoleKeys.Owner))
        {
            var activeOwnerCount = await db.Users
                .CountAsync(u => u.IsActive && u.UserRoles.Any(ur => ur.Role.Key == RoleKeys.Owner), ct);

            if (activeOwnerCount <= 1)
            {
                return Results.Problem(
                    title: "Owner-и охирин",
                    detail: "Owner-и охиринро ғайрифаъол кардан мумкин нест.",
                    statusCode: StatusCodes.Status409Conflict);
            }
        }

        user.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static UserListItem ToListItem(User user) => new(
        user.Id,
        user.FullName,
        user.Username,
        user.IsActive,
        user.UserRoles.Select(ur => new RoleSummary(ur.Role.Id, ur.Role.Key, ur.Role.Name)).ToList());

    private static UserDetail ToDetail(User user) => new(
        user.Id,
        user.FullName,
        user.Username,
        user.Phone,
        user.AvatarUrl,
        user.IsActive,
        user.MustChangePassword,
        user.OnlyAssigned,
        user.UserRoles.Select(ur => new RoleSummary(ur.Role.Id, ur.Role.Key, ur.Role.Name)).ToList(),
        user.UserPermissions.Select(up => new UserPermissionExceptionDto(up.PermissionKey, up.IsGranted)).ToList());
}

public record SetActiveRequest(bool IsActive);
