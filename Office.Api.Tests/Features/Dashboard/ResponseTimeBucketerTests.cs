using Office.Api.Features.Dashboard;

namespace Office.Api.Tests.Features.Dashboard;

public class ResponseTimeBucketerTests
{
    [Theory]
    [InlineData(0, ResponseTimeBucketer.Under5Min)]
    [InlineData(4 * 60, ResponseTimeBucketer.Under5Min)]
    [InlineData(5 * 60, ResponseTimeBucketer.From5To15Min)] // марз худаш дар сабади навбатӣ
    [InlineData(14 * 60, ResponseTimeBucketer.From5To15Min)]
    [InlineData(15 * 60, ResponseTimeBucketer.From15To60Min)]
    [InlineData(59 * 60, ResponseTimeBucketer.From15To60Min)]
    [InlineData(60 * 60, ResponseTimeBucketer.From1To4Hours)]
    [InlineData(3 * 60 * 60 + 59 * 60, ResponseTimeBucketer.From1To4Hours)]
    [InlineData(4 * 60 * 60, ResponseTimeBucketer.Over4Hours)]
    [InlineData(24 * 60 * 60, ResponseTimeBucketer.Over4Hours)]
    public void Bucket_BoundaryAndMidpointValues_ReturnsExpectedBucket(int seconds, string expected)
    {
        Assert.Equal(expected, ResponseTimeBucketer.Bucket(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Bucket_NegativeDuration_ThrowsInsteadOfReturningAWrongBucket()
    {
        // Query-и SQL сохторан набояд манфӣ диҳад (ниг. DashboardStatsQueryService) — агар
        // расид, хатои воқеӣ аст, бояд фавран намоён шавад, на хомӯшона ба "<5min" афтад.
        Assert.Throws<ArgumentOutOfRangeException>(() => ResponseTimeBucketer.Bucket(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void AllBuckets_ListsExactlyFiveBucketsInOrder()
    {
        Assert.Equal(
            [ResponseTimeBucketer.Under5Min, ResponseTimeBucketer.From5To15Min, ResponseTimeBucketer.From15To60Min,
                ResponseTimeBucketer.From1To4Hours, ResponseTimeBucketer.Over4Hours],
            ResponseTimeBucketer.AllBuckets);
    }
}
