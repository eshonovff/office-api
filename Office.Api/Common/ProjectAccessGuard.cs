using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Data;

namespace Office.Api.Common;

public interface IProjectAccessGuard
{
    Task<bool> HasAccessAsync(ClaimsPrincipal principal, Guid projectId, CancellationToken ct);
}

public class ProjectAccessGuard(AppDbContext db) : IProjectAccessGuard
{
    public static bool CanSeeAllProjects(ClaimsPrincipal principal) =>
        principal.IsInRole(RoleKeys.Owner) || principal.IsInRole(RoleKeys.Admin);

    public async Task<bool> HasAccessAsync(ClaimsPrincipal principal, Guid projectId, CancellationToken ct)
    {
        if (CanSeeAllProjects(principal))
            return true;

        var userId = principal.GetUserId();
        return await db.ProjectMembers.AnyAsync(pm => pm.ProjectId == projectId && pm.UserId == userId, ct);
    }
}
