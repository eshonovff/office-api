using Microsoft.EntityFrameworkCore;
using Office.Api.Data;
using Office.Api.Data.Entities;

namespace Office.Api.Features.Subscriptions;

/// <summary>What a мизоҷ may use right now — the plan limits behind every paid feature check.</summary>
public static class CustomerEntitlements
{
    /// <summary>
    /// The limits that apply to this access: the paid tier's, or Pro's during the trial (user
    /// decision, 2026-09-24). Null = no access at all — expired, or the tier isn't in the catalog
    /// (a config error fails closed, never open).
    /// </summary>
    public static PlanLimitsOptions? ResolveLimits(CustomerAccess access, SubscriptionCatalog catalog)
    {
        if (!access.HasAccess)
            return null;

        var tier = access.Status == CustomerAccessStatus.Trial ? CustomerPlanTier.Pro : access.Tier;
        return tier is null ? null : catalog.Plans.FirstOrDefault(p => p.Tier == tier)?.Limits;
    }

    /// <summary>The caller's current limits (null = no access), computed from the stored plan fields.</summary>
    public static async Task<PlanLimitsOptions?> LoadLimitsAsync(
        Guid customerId, AppDbContext db, IConfiguration configuration, CancellationToken ct)
    {
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId, ct);
        if (customer is null)
            return null;

        var access = CustomerAccessResolver.Resolve(
            DateTimeOffset.UtcNow, customer.TrialEndsAt, customer.PlanTier, customer.PlanExpiresAt);
        return ResolveLimits(access, SubscriptionCatalog.Load(configuration));
    }

    /// <summary>Whether one more can be added: a null limit is unlimited.</summary>
    public static bool CanAddOneMore(int? limit, int used) => limit is null || used < limit;

    /// <summary>
    /// Null when one more active automation fits the caller's plan; otherwise the 403 to return.
    /// Automations are the flows and the comment auto-reply rules together — one budget, or the
    /// rules would be a way around the plan. The tenant filter scopes both counts to the caller.
    /// </summary>
    public static async Task<IResult?> CheckCanActivateOneMoreAutomationAsync(
        Guid customerId, AppDbContext db, IConfiguration configuration, CancellationToken ct)
    {
        var limits = await LoadLimitsAsync(customerId, db, configuration, ct);
        if (limits is null)
            return NoAccessProblem();

        var active = await db.Flows.CountAsync(f => f.IsActive, ct) + await db.AutomationRules.CountAsync(r => r.IsActive, ct);
        return CanAddOneMore(limits.ActiveAutomations, active)
            ? null
            : LimitReachedProblem(
                $"Тарифи шумо то {limits.ActiveAutomations} автоматизатсияи фаъол иҷозат медиҳад. " +
                "Якеашро хомӯш кунед ё тарифро баланд кунед.");
    }

    public static IResult NoAccessProblem() => Results.Problem(
        title: "Тариф фаъол нест",
        detail: "Мӯҳлати шумо гузаштааст. Барои идомаи кор тариф харед.",
        statusCode: StatusCodes.Status403Forbidden,
        extensions: new Dictionary<string, object?> { ["code"] = "no_access" });

    public static IResult LimitReachedProblem(string detail) => Results.Problem(
        title: "Маҳдудияти тариф",
        detail: detail,
        statusCode: StatusCodes.Status403Forbidden,
        extensions: new Dictionary<string, object?> { ["code"] = "plan_limit" });
}
