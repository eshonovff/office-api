using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;
using Office.Api.Auth;
using Office.Api.Common;

namespace Office.Api.Features.Dashboard;

/// <summary>
/// GET /api/dashboard — саҳифаи асосии office.nizom.tj. Ҳоло танҳо блоки actionRequired
/// (тибқи тартиби корӣ: аввал ин, тасдиқ, баъд myWork/teamStats). Ҳисоби воқеӣ дар
/// DashboardQueryService — ин ҷо танҳо кэш (60 сония) + HTTP.
/// </summary>
public static class DashboardEndpoints
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/dashboard", GetAsync)
            .WithTags("Dashboard")
            .RequirePermission(Permissions.Inbox.View)
            .WithSummary("Дашборд — саҳифаи асосӣ (actionRequired ҳоло, myWork/teamStats дар оянда)")
            .Produces<DashboardResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> GetAsync(
        ClaimsPrincipal principal, DashboardQueryService queryService, IMemoryCache cache, CancellationToken ct)
    {
        // Калид аз userId + нақшҳо (на танҳо userId) — натиҷа вобаста ба дастрасӣ фарқ мекунад
        // (Owner/Admin бештар мебинанд аз operator-и муқаррарӣ), пас нақшҳо низ бояд ба cache
        // key дохил шаванд, вагарна корбари якум натиҷаи худро ба дигаре "мерос" мегузорад.
        var roles = string.Join(',', principal.FindAll(ClaimTypes.Role).Select(c => c.Value).OrderBy(r => r, StringComparer.Ordinal));
        var cacheKey = $"dashboard:{principal.GetUserId()}:{roles}";

        if (cache.TryGetValue<DashboardResponse>(cacheKey, out var cached) && cached is not null)
            return Results.Ok(cached);

        var response = await queryService.ComputeAsync(principal, ct);
        cache.Set(cacheKey, response, CacheDuration);

        return Results.Ok(response);
    }
}
