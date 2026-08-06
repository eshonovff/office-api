using Hangfire.Dashboard;
using Office.Api.Auth;

namespace Office.Api.Channels;

/// <summary>Дастрасӣ ба /hangfire танҳо барои Owner.</summary>
public class OwnerOnlyDashboardAuthFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        return httpContext.User.Identity?.IsAuthenticated == true && httpContext.User.IsInRole(RoleKeys.Owner);
    }
}
