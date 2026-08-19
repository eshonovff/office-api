using Office.Api.Channels.Meta;

namespace Office.Api.Tests.Channels.Meta;

public class SensitiveUrlRedactorTests
{
    [Fact]
    public void Redact_ClientSecret_IsMasked()
    {
        const string url = "https://graph.instagram.com/access_token?grant_type=ig_exchange_token&client_secret=abc123&access_token=xyz789";

        var result = SensitiveUrlRedactor.Redact(url);

        Assert.Equal(
            "https://graph.instagram.com/access_token?grant_type=ig_exchange_token&client_secret=REDACTED&access_token=REDACTED",
            result);
    }

    [Fact]
    public void Redact_CodeParam_IsMasked()
    {
        const string url = "https://api.instagram.com/oauth/access_token?code=a-real-authorization-code&redirect_uri=https://x";

        var result = SensitiveUrlRedactor.Redact(url);

        Assert.Contains("code=REDACTED", result);
        Assert.DoesNotContain("a-real-authorization-code", result);
    }

    [Fact]
    public void Redact_NonSensitiveParamsAndPath_AreUntouched()
    {
        const string url = "https://graph.instagram.com/v23.0/me?fields=id,username&access_token=xyz789";

        var result = SensitiveUrlRedactor.Redact(url);

        Assert.StartsWith("https://graph.instagram.com/v23.0/me?fields=id,username&access_token=REDACTED", result);
    }

    [Fact]
    public void Redact_NoSensitiveParams_ReturnsUnchanged()
    {
        const string url = "https://api.instagram.com/oauth/access_token";

        Assert.Equal(url, SensitiveUrlRedactor.Redact(url));
    }
}
