using Office.Api.Channels.Messenger;

namespace Office.Api.Tests.Channels.Messenger;

public class MessengerSendModePlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Plan_NoWindowEverSet_ReturnsPlain()
    {
        Assert.Equal(MessengerSendMode.Plain, MessengerSendModePlanner.Plan(null, Now));
    }

    [Fact]
    public void Plan_WindowStillOpen_ReturnsPlain()
    {
        Assert.Equal(MessengerSendMode.Plain, MessengerSendModePlanner.Plan(Now.AddHours(1), Now));
    }

    [Fact]
    public void Plan_WindowJustClosed_ReturnsTag()
    {
        Assert.Equal(MessengerSendMode.Tag, MessengerSendModePlanner.Plan(Now.AddSeconds(-1), Now));
    }

    [Fact]
    public void Plan_WithinSevenDaysOfLastInbound_ReturnsTag()
    {
        // windowExpiresAt = lastInbound + 24h, so 5 days after that is still day 6 overall.
        Assert.Equal(MessengerSendMode.Tag, MessengerSendModePlanner.Plan(Now.AddDays(-5), Now));
    }

    [Fact]
    public void Plan_ExactlySevenDaysFromLastInbound_ReturnsTag()
    {
        // windowExpiresAt (lastInbound + 24h) + 6d == lastInbound + 7d == now.
        Assert.Equal(MessengerSendMode.Tag, MessengerSendModePlanner.Plan(Now.AddDays(-6), Now));
    }

    [Fact]
    public void Plan_PastSevenDaysFromLastInbound_ReturnsReject()
    {
        Assert.Equal(MessengerSendMode.Reject, MessengerSendModePlanner.Plan(Now.AddDays(-6).AddSeconds(-1), Now));
    }
}
