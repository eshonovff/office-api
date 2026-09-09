using Microsoft.Extensions.Caching.Memory;
using Office.Api.Auth;

namespace Office.Api.Features.Dashboard;

/// <summary>
/// GET /api/dashboard/stats (Блоки 3) — endpoint-и АЛОҲИДА аз GET /api/dashboard, қасдан:
/// вазнинтар аст (ҳашт агрегат) ва танҳо ҳангоми кушодани таби "Статистика" даркор мешавад.
/// Кэши 5 дақиқа (на 60 сония-и /api/dashboard) — маълумот суст тағйир меёбад.
/// </summary>
public static class StatsEndpoints
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public static IEndpointRouteBuilder MapDashboardStatsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/dashboard/stats", GetAsync)
            .WithTags("Dashboard")
            .RequireOwnerOrAdmin()
            .WithSummary("Дашборд — Блоки 3 (статистикаи тим), Owner/Admin танҳо")
            .Produces<DashboardStatsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> GetAsync(
        int? days, DashboardStatsQueryService queryService, IMemoryCache cache, CancellationToken ct)
    {
        var resolvedDays = days ?? 14;
        if (!StatsDaysRange.IsValid(resolvedDays))
        {
            return Results.Problem(
                title: "Қимати ?days= нодуруст",
                detail: $"Қиматҳои иҷозатдодашуда: {string.Join(", ", StatsDaysRange.Allowed)}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // На аз рӯи userId — ҳама Owner/Admin ҳамон маълумоти якхела мебинанд (тим-вокеъ, бе
        // филтри шахсӣ), пас як cache entry барои ҳама кофист, на як барои ҳар корбар.
        var cacheKey = $"dashboard-stats:{resolvedDays}";

        if (cache.TryGetValue<DashboardStatsResponse>(cacheKey, out var cached) && cached is not null)
            return Results.Ok(cached);

        var response = await queryService.ComputeAsync(resolvedDays, ct);
        cache.Set(cacheKey, response, CacheDuration);

        return Results.Ok(response);
    }
}
