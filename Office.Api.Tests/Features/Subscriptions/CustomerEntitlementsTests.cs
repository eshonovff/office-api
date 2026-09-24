using Office.Api.Data.Entities;
using Office.Api.Features.Subscriptions;

namespace Office.Api.Tests.Features.Subscriptions;

public class CustomerEntitlementsTests
{
    private static readonly DateTimeOffset EndsAt = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly SubscriptionCatalog Catalog = new(
        TrialDays: 7,
        PaymentWindow: TimeSpan.FromMinutes(5),
        Currency: "TJS",
        Durations: [new SubscriptionDurationOptions { Months = 1 }],
        Plans:
        [
            new SubscriptionPlanOptions { Tier = CustomerPlanTier.Pro, Limits = new PlanLimitsOptions { Accounts = 1, ActiveAutomations = 10 } },
            new SubscriptionPlanOptions { Tier = CustomerPlanTier.Creator, Limits = new PlanLimitsOptions { Accounts = 2 } },
        ],
        PaymentCards: []);

    [Fact]
    public void Trial_GetsProLimits()
    {
        var limits = CustomerEntitlements.ResolveLimits(new CustomerAccess(CustomerAccessStatus.Trial, null, EndsAt), Catalog);

        Assert.NotNull(limits);
        Assert.Equal(1, limits.Accounts);
        Assert.Equal(10, limits.ActiveAutomations);
    }

    [Fact]
    public void ActivePlan_GetsItsOwnTiersLimits()
    {
        var limits = CustomerEntitlements.ResolveLimits(
            new CustomerAccess(CustomerAccessStatus.Active, CustomerPlanTier.Creator, EndsAt), Catalog);

        Assert.Equal(2, limits!.Accounts);
        Assert.Null(limits.ActiveAutomations); // unlimited on Creator
    }

    [Fact]
    public void Expired_HasNoEntitlements()
    {
        Assert.Null(CustomerEntitlements.ResolveLimits(
            new CustomerAccess(CustomerAccessStatus.Expired, CustomerPlanTier.Pro, EndsAt), Catalog));
    }

    [Fact]
    public void TierMissingFromCatalog_FailsClosed()
    {
        Assert.Null(CustomerEntitlements.ResolveLimits(
            new CustomerAccess(CustomerAccessStatus.Active, CustomerPlanTier.Premium, EndsAt), Catalog));
    }

    [Theory]
    [InlineData(1, 0, true)]
    [InlineData(1, 1, false)]
    [InlineData(10, 9, true)]
    [InlineData(10, 10, false)]
    [InlineData(null, 1000, true)]
    public void CanAddOneMore_RespectsTheLimit(int? limit, int used, bool expected)
    {
        Assert.Equal(expected, CustomerEntitlements.CanAddOneMore(limit, used));
    }
}
