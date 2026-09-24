using Office.Api.Data.Entities;
using Office.Api.Features.Subscriptions;

namespace Office.Api.Tests.Features.Subscriptions;

public class CustomerAccessResolverTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Resolve_TrialRunning_NoPlan_ReturnsTrial()
    {
        var access = CustomerAccessResolver.Resolve(Now, Now.AddDays(3), planTier: null, planExpiresAt: null);

        Assert.Equal(CustomerAccessStatus.Trial, access.Status);
        Assert.Null(access.Tier);
        Assert.Equal(Now.AddDays(3), access.EndsAt);
        Assert.True(access.HasAccess);
    }

    [Fact]
    public void Resolve_TrialOver_NoPlan_ReturnsExpiredWithTrialEnd()
    {
        var access = CustomerAccessResolver.Resolve(Now, Now.AddDays(-1), planTier: null, planExpiresAt: null);

        Assert.Equal(CustomerAccessStatus.Expired, access.Status);
        Assert.Equal(Now.AddDays(-1), access.EndsAt);
        Assert.False(access.HasAccess);
    }

    [Fact]
    public void Resolve_PlanActive_ReturnsActiveWithTier()
    {
        var access = CustomerAccessResolver.Resolve(Now, Now.AddDays(-10), CustomerPlanTier.Pro, Now.AddDays(20));

        Assert.Equal(CustomerAccessStatus.Active, access.Status);
        Assert.Equal(CustomerPlanTier.Pro, access.Tier);
        Assert.Equal(Now.AddDays(20), access.EndsAt);
    }

    [Fact]
    public void Resolve_PaidDuringTrial_ReportsActiveNotTrial()
    {
        var access = CustomerAccessResolver.Resolve(Now, Now.AddDays(5), CustomerPlanTier.Creator, Now.AddDays(35));

        Assert.Equal(CustomerAccessStatus.Active, access.Status);
        Assert.Equal(CustomerPlanTier.Creator, access.Tier);
    }

    [Fact]
    public void Resolve_PlanExpired_KeepsLastTierForTheUi()
    {
        var access = CustomerAccessResolver.Resolve(Now, Now.AddDays(-40), CustomerPlanTier.Premium, Now.AddDays(-1));

        Assert.Equal(CustomerAccessStatus.Expired, access.Status);
        Assert.Equal(CustomerPlanTier.Premium, access.Tier);
        Assert.Equal(Now.AddDays(-1), access.EndsAt);
        Assert.False(access.HasAccess);
    }

    [Fact]
    public void Resolve_ExactlyAtPlanEnd_IsExpired()
    {
        var access = CustomerAccessResolver.Resolve(Now, trialEndsAt: null, CustomerPlanTier.Pro, Now);

        Assert.Equal(CustomerAccessStatus.Expired, access.Status);
    }

    [Fact]
    public void Resolve_NoTrialNoPlan_ReturnsExpired()
    {
        var access = CustomerAccessResolver.Resolve(Now, trialEndsAt: null, planTier: null, planExpiresAt: null);

        Assert.Equal(CustomerAccessStatus.Expired, access.Status);
        Assert.Null(access.EndsAt);
    }
}
