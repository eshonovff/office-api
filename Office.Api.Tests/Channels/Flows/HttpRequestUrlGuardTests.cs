using Office.Api.Channels.Flows;

namespace Office.Api.Tests.Channels.Flows;

public class HttpRequestUrlGuardTests
{
    [Theory]
    [InlineData("https://api.example.com/webhook", true)]
    [InlineData("https://example.com", true)]
    [InlineData("http://api.example.com/webhook", false)] // https only
    [InlineData("https://localhost/x", false)]
    [InlineData("https://127.0.0.1/x", false)]
    [InlineData("https://10.0.0.5/x", false)]
    [InlineData("https://192.168.1.1/x", false)]
    [InlineData("https://172.16.0.1/x", false)]
    [InlineData("https://169.254.169.254/x", false)] // cloud metadata endpoint
    [InlineData("https://8.8.8.8/x", true)]
    [InlineData("not a url", false)]
    public void IsAllowed_ChecksSchemeAndPrivateRanges(string url, bool expected)
    {
        Assert.Equal(expected, HttpRequestUrlGuard.IsAllowed(url));
    }

    [Fact]
    public void IsAllowed_Ipv6Loopback_IsBlocked()
    {
        Assert.False(HttpRequestUrlGuard.IsAllowed("https://[::1]/x"));
    }

    [Theory]
    [InlineData("https://0.0.0.0/x")]                 // reaches localhost on Linux
    [InlineData("https://[::ffff:127.0.0.1]/x")]      // IPv4-mapped loopback
    [InlineData("https://[fd00::1]/x")]               // IPv6 unique local
    [InlineData("https://[fe80::1]/x")]               // IPv6 link-local
    [InlineData("https://100.64.0.1/x")]              // carrier-grade NAT
    [InlineData("https://api.localhost/x")]           // *.localhost resolves to loopback
    [InlineData("https://LOCALHOST./x")]
    public void IsAllowed_BlocksLiteralNonPublicTargetsTheOldCheckMissed(string url)
    {
        Assert.False(HttpRequestUrlGuard.IsAllowed(url));
    }
}
