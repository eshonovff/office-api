using Office.Api.Channels.Flows;

namespace Office.Api.Tests.Channels.Flows;

public class FlowSessionLoopGuardTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(199, false)]
    [InlineData(200, true)]
    [InlineData(201, true)]
    public void ShouldStop_AtOrAboveMaxSteps_ReturnsTrue(int stepCount, bool expected)
    {
        Assert.Equal(200, FlowSessionLoopGuard.MaxSteps); // фарзи теорияи поён — агар тағйир ёбад, ин ҷо низ бояд нав шавад
        Assert.Equal(expected, FlowSessionLoopGuard.ShouldStop(stepCount));
    }
}
