using Office.Api.Features.Subscriptions;

namespace Office.Api.Tests.Features.Subscriptions;

public class SubscriptionPeriodCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CalculateNewExpiry_NothingRunning_StartsNow()
    {
        var expiry = SubscriptionPeriodCalculator.CalculateNewExpiry(Now, null, Now.AddDays(-1), months: 1);

        Assert.Equal(Now.AddMonths(1), expiry);
    }

    [Fact]
    public void CalculateNewExpiry_PaidDuringTrial_KeepsRemainingTrialDays()
    {
        var trialEnd = Now.AddDays(4);

        var expiry = SubscriptionPeriodCalculator.CalculateNewExpiry(Now, null, trialEnd, months: 1);

        Assert.Equal(trialEnd.AddMonths(1), expiry);
    }

    [Fact]
    public void CalculateNewExpiry_RenewBeforePlanEnds_ExtendsFromCurrentEnd()
    {
        var currentEnd = Now.AddDays(10);

        var expiry = SubscriptionPeriodCalculator.CalculateNewExpiry(Now, currentEnd, Now.AddDays(-30), months: 3);

        Assert.Equal(currentEnd.AddMonths(3), expiry);
    }

    [Fact]
    public void CalculateNewExpiry_RenewAfterPlanEnded_StartsNow()
    {
        var expiry = SubscriptionPeriodCalculator.CalculateNewExpiry(Now, Now.AddDays(-5), Now.AddDays(-40), months: 12);

        Assert.Equal(Now.AddMonths(12), expiry);
    }
}
