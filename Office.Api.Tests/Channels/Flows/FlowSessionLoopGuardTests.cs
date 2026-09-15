using Office.Api.Channels.Flows;

namespace Office.Api.Tests.Channels.Flows;

public class FlowSessionLoopGuardTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(49, false)]
    [InlineData(50, true)]
    [InlineData(51, true)]
    public void ShouldStop_AtOrAboveFifty_ReturnsTrue(int stepCount, bool expected)
    {
        Assert.Equal(expected, FlowSessionLoopGuard.ShouldStop(stepCount));
    }
}
