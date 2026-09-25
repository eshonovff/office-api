using Office.Api.Channels.Flows;
using Office.Api.Data.Entities;
using Office.Api.Features.Subscriptions;

namespace Office.Api.Tests.Channels.Flows;

public class AutomationRunGateTests
{
    private static readonly CustomerAccess Trial = new(CustomerAccessStatus.Trial, null, DateTimeOffset.UtcNow.AddDays(3));
    private static readonly CustomerAccess Expired = new(CustomerAccessStatus.Expired, CustomerPlanTier.Pro, DateTimeOffset.UtcNow.AddDays(-1));

    [Fact]
    public void CompanyChannel_AlwaysRuns() =>
        Assert.True(AutomationRunGate.CanRun(isCompanyChannel: true, channelIsActive: false, ownerAccess: null));

    [Fact]
    public void MizojChannel_WithAccessAndConnected_Runs() => Assert.True(AutomationRunGate.CanRun(false, true, Trial));

    [Fact]
    public void MizojChannel_Expired_DoesNotRun() => Assert.False(AutomationRunGate.CanRun(false, true, Expired));

    [Fact]
    public void MizojChannel_Disconnected_DoesNotRun() => Assert.False(AutomationRunGate.CanRun(false, false, Trial));

    [Fact]
    public void MizojChannel_UnknownAccess_FailsClosed() => Assert.False(AutomationRunGate.CanRun(false, true, null));
}
