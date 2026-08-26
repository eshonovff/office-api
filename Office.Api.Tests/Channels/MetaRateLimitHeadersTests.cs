using System.Net.Http;
using Office.Api.Channels;

namespace Office.Api.Tests.Channels;

public class MetaRateLimitHeadersTests
{
    [Fact]
    public void Describe_NoRelevantHeaders_ReturnsNull()
    {
        using var response = new HttpResponseMessage();
        response.Headers.Add("X-Fb-Trace-Id", "abc123");

        Assert.Null(MetaRateLimitHeaders.Describe(response.Headers));
    }

    [Fact]
    public void Describe_UsageHeaderPresent_IncludesItInTheResult()
    {
        using var response = new HttpResponseMessage();
        response.Headers.Add("X-App-Usage", """{"call_count":80,"total_time":60,"total_cputime":50}""");

        var described = MetaRateLimitHeaders.Describe(response.Headers);

        Assert.NotNull(described);
        Assert.Contains("X-App-Usage=", described);
        Assert.Contains("call_count", described);
    }

    [Fact]
    public void Describe_RetryAfterPresent_IncludesIt()
    {
        using var response = new HttpResponseMessage();
        response.Headers.Add("Retry-After", "30");

        Assert.Equal("Retry-After=30", MetaRateLimitHeaders.Describe(response.Headers));
    }

    [Fact]
    public void TryGetCallVolumePercent_NoHeader_ReturnsNull()
    {
        using var response = new HttpResponseMessage();

        Assert.Null(MetaRateLimitHeaders.TryGetCallVolumePercent(response.Headers));
    }

    [Fact]
    public void TryGetCallVolumePercent_HeaderPresent_ParsesCallVolume()
    {
        using var response = new HttpResponseMessage();
        response.Headers.Add("X-App-Usage", """{"call_volume":42,"total_time":10,"total_cputime":5}""");

        Assert.Equal(42, MetaRateLimitHeaders.TryGetCallVolumePercent(response.Headers));
    }

    [Fact]
    public void TryGetCallVolumePercent_MalformedJson_ReturnsNullInsteadOfThrowing()
    {
        using var response = new HttpResponseMessage();
        response.Headers.Add("X-App-Usage", "not json");

        Assert.Null(MetaRateLimitHeaders.TryGetCallVolumePercent(response.Headers));
    }
}
