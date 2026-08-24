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
}
