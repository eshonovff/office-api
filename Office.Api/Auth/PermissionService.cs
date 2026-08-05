using Microsoft.EntityFrameworkCore;
using Office.Api.Data;

namespace Office.Api.Auth;

public record PermissionResolution(
    IReadOnlySet<string> Permissions,
    IReadOnlyList<string> Roles,
    int PermissionsVersion,
    bool IsOwner);

public interface IPermissionService
{
    Task<PermissionResolution> ResolveAsync(Guid userId, CancellationToken ct);
    Task BumpVersionAsync(Guid userId, CancellationToken ct);
}

public class PermissionService(AppDbContext db) : IPermissionService
{
    public async Task<PermissionResolution> ResolveAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users
            .AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role).ThenInclude(r => r.RolePermissions)
            .Include(u => u.UserPermissions)
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new InvalidOperationException("Корбар ёфт нашуд.");

        var roleKeys = user.UserRoles.Select(ur => ur.Role.Key).ToList();
        var isOwner = roleKeys.Contains(RoleKeys.Owner);

        var rolePermissionKeys = user.UserRoles.SelectMany(ur => ur.Role.RolePermissions.Select(rp => rp.PermissionKey));
        var exceptions = user.UserPermissions.Select(up => (up.PermissionKey, up.IsGranted));

        var granted = PermissionResolver.Resolve(rolePermissionKeys, exceptions, isOwner);

        return new PermissionResolution(granted, roleKeys, user.PermissionsVersion, isOwner);
    }

    public async Task BumpVersionAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new InvalidOperationException("Корбар ёфт нашуд.");

        user.PermissionsVersion += 1;

        var activeTokens = await db.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAt == null)
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var token in activeTokens)
            token.RevokedAt = now;

        await db.SaveChangesAsync(ct);
    }
}
