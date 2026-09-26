using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Data;

namespace Office.Api.Features.CustomerAnalytics;

/// <summary>
/// A мизоҷ's analytics (phase 19): what their automations, comments, broadcasts and chats did
/// in a period, next to the period before. Who may see what is the tenant filter's — every
/// number is made only of the caller's own rows (CustomerAnalyticsQueries); another мизоҷ's
/// account or automation is "not found". Reading its own numbers needs no plan, like reading
/// its contacts and chats. A long period is a heavy read, so it is bounded (AnalyticsPeriod)
/// and rate-limited.
/// </summary>
public static class CustomerAnalyticsEndpoints
{
    public const string RateLimitPolicy = "customer-analytics";

    public static IEndpointRouteBuilder MapCustomerAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var analytics = app.MapGroup("/api/public/analytics")
            .WithTags("CustomerAnalytics")
            .RequireAuthorization(AuthSchemes.CustomerOnlyPolicy)
            .RequireRateLimiting(RateLimitPolicy);

        analytics.MapGet("/overview", OverviewAsync)
            .WithSummary("Рақамҳои умумӣ, рӯзҳо, рассылкаҳо, постҳои беҳтарин — бо давраи пештара")
            .Produces<AnalyticsOverview>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        analytics.MapGet("/automations", AutomationsAsync)
            .WithSummary("Ҳар автоматизатсия (оғоз / мақсад) ва ҳар қоидаи шарҳ")
            .Produces<AnalyticsAutomations>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        analytics.MapGet("/automations/{flowId:guid}", FlowDetailAsync)
            .WithSummary("Як автоматизатсия: рӯзҳо ва охирин оғозкунандагон")
            .Produces<AnalyticsFlowDetail>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    public static async Task<IResult> OverviewAsync(DateOnly? from, DateOnly? to, Guid? channelId, AppDbContext db, CancellationToken ct)
    {
        var (period, error) = AnalyticsPeriod.Parse(from, to, DateTimeOffset.UtcNow);
        if (error is not null)
            return PeriodProblem(error);
        if (channelId is not null && !await db.Channels.AnyAsync(c => c.Id == channelId, ct))
            return Results.NotFound();

        return Results.Ok(await CustomerAnalyticsQueries.OverviewAsync(db, period, channelId, ct));
    }

    public static async Task<IResult> AutomationsAsync(DateOnly? from, DateOnly? to, Guid? channelId, AppDbContext db, CancellationToken ct)
    {
        var (period, error) = AnalyticsPeriod.Parse(from, to, DateTimeOffset.UtcNow);
        if (error is not null)
            return PeriodProblem(error);
        if (channelId is not null && !await db.Channels.AnyAsync(c => c.Id == channelId, ct))
            return Results.NotFound();

        return Results.Ok(await CustomerAnalyticsQueries.AutomationsAsync(db, period, channelId, ct));
    }

    public static async Task<IResult> FlowDetailAsync(Guid flowId, DateOnly? from, DateOnly? to, AppDbContext db, CancellationToken ct)
    {
        var (period, error) = AnalyticsPeriod.Parse(from, to, DateTimeOffset.UtcNow);
        if (error is not null)
            return PeriodProblem(error);

        var detail = await CustomerAnalyticsQueries.FlowDetailAsync(db, flowId, period, ct);
        return detail is null ? Results.NotFound() : Results.Ok(detail);
    }

    private static IResult PeriodProblem(string error) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["period"] = [error] });
}
