using Office.Api.Features.Tasks;

namespace Office.Api.Tests.Tasks;

public class PositionCalculatorTests
{
    [Fact]
    public void Calculate_EmptyColumn_ReturnsStep()
    {
        var result = PositionCalculator.Calculate(null, null);

        Assert.Equal(PositionCalculator.Step, result);
    }

    [Fact]
    public void Calculate_DroppedFirst_ReturnsHalfOfExistingFirst()
    {
        var result = PositionCalculator.Calculate(beforePosition: null, afterPosition: 1000);

        Assert.Equal(500, result);
    }

    [Fact]
    public void Calculate_DroppedBetweenNeighbors_ReturnsAverage()
    {
        var result = PositionCalculator.Calculate(beforePosition: 1000, afterPosition: 2000);

        Assert.Equal(1500, result);
    }

    [Fact]
    public void Calculate_DroppedLast_ReturnsExistingLastPlusStep()
    {
        var result = PositionCalculator.Calculate(beforePosition: 2000, afterPosition: null);

        Assert.Equal(3000, result);
    }

    [Fact]
    public void NeedsReindex_NeighborsTooClose_ReturnsTrue()
    {
        var computed = PositionCalculator.Calculate(1000.00001, 1000.00002);

        Assert.True(PositionCalculator.NeedsReindex(1000.00001, 1000.00002, computed));
    }

    [Fact]
    public void NeedsReindex_NormalGap_ReturnsFalse()
    {
        var computed = PositionCalculator.Calculate(1000, 2000);

        Assert.False(PositionCalculator.NeedsReindex(1000, 2000, computed));
    }
}
