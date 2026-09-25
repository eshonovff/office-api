using Microsoft.EntityFrameworkCore;
using Office.Api.Data;

namespace Office.Api.Auth;

/// <summary>
/// Active staff whose effective permissions include a key — the same formula as their token
/// (PermissionResolver: roles + personal grants − personal denials; Owner has everything), so
/// "who gets told" never drifts from "who may act". Staff counts are small: resolved in memory.
/// </summary>
public static class StaffWithPermission
{
    public static async Task<List<Guid>> FindUserIdsAsync(AppDbContext db, string permission, CancellationToken ct)
    {
        var users = await db.Users
            .AsNoTracking()
            .Where(u => u.IsActive)
            .Select(u => new
            {
                u.Id,
                IsOwner = u.UserRoles.Any(ur => ur.Role.Key == RoleKeys.Owner),
                RolePermissions = u.UserRoles.SelectMany(ur => ur.Role.RolePermissions.Select(rp => rp.PermissionKey)).ToList(),
                Exceptions = u.UserPermissions.Select(up => new { up.PermissionKey, up.IsGranted }).ToList(),
            })
            .ToListAsync(ct);

        return users
            .Where(u => PermissionResolver
                .Resolve(u.RolePermissions, u.Exceptions.Select(e => (e.PermissionKey, e.IsGranted)), u.IsOwner)
                .Contains(permission))
            .Select(u => u.Id)
            .ToList();
    }
}
