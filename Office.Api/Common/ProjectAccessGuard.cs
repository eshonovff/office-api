using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Data;

namespace Office.Api.Common;

public class ProjectAccessGuard(AppDbContext db)
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
