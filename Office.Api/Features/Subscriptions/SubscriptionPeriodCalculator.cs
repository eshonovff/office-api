namespace Office.Api.Features.Subscriptions;

public static class SubscriptionPeriodCalculator
{
    /// <summary>
    /// The new period starts at the latest of now / current plan end / trial end, so paying
    /// early — during the trial or before a plan runs out — never costs the customer days.
    /// Tier changes mid-period are not prorated: the remaining days simply carry over at the
    /// new tier (moderators approve every request by hand anyway).
    /// </summary>
    public static DateTimeOffset CalculateNewExpiry(
        DateTimeOffset now,
        DateTimeOffset? currentPlanExpiresAt,
        DateTimeOffset? trialEndsAt,
        int months)
    {
        var start = now;
        if (currentPlanExpiresAt is not null && currentPlanExpiresAt.Value > start)
            start = currentPlanExpiresAt.Value;
        if (trialEndsAt is not null && trialEndsAt.Value > start)
            start = trialEndsAt.Value;

        return start.AddMonths(months);
    }
}
