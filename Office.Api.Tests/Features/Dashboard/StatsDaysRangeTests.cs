using Office.Api.Features.Dashboard;

namespace Office.Api.Tests.Features.Dashboard;

public class StatsDaysRangeTests
{
    [Theory]
    [InlineData(7)]
    [InlineData(14)]
    [InlineData(30)]
    [InlineData(90)]
    public void IsValid_AllowedValue_ReturnsTrue(int days) => Assert.True(StatsDaysRange.IsValid(days));

    [Theory]
    [InlineData(13)]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-7)]
    [InlineData(365)]
    public void IsValid_DisallowedValue_ReturnsFalse(int days) => Assert.False(StatsDaysRange.IsValid(days));
}
