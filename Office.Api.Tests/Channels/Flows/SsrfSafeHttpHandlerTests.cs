using System.Net;
using Office.Api.Channels.Flows;

namespace Office.Api.Tests.Channels.Flows;

public class SsrfSafeHttpHandlerTests
{
    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("10.1.2.3")]
    [InlineData("127.0.0.1")]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.255")]
    [InlineData("169.254.169.254")] // cloud metadata
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.0.1")]
    [InlineData("192.0.2.1")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("::ffff:10.0.0.1")]  // IPv4-mapped private
    [InlineData("64:ff9b::a00:1")]   // NAT64 of 10.0.0.1
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("fe80::1")]
    [InlineData("ff02::1")]
    [InlineData("2001:db8::1")]
    public void NonPublicAddresses_AreRefused(string address)
    {
        Assert.False(PublicAddressPolicy.IsPublic(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("100.63.255.255")]  // just below CGNAT
    [InlineData("172.32.0.1")]      // just above 172.16/12
    [InlineData("2a00:1450:4001::1")]
    [InlineData("64:ff9b::808:808")] // NAT64 of 8.8.8.8
    public void PublicAddresses_AreAllowed(string address)
    {
        Assert.True(PublicAddressPolicy.IsPublic(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("localhost")]  // a NAME that resolves to loopback — what the URL check can't see
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public async Task Resolve_RefusesHostsThatEndUpNonPublic(string host)
    {
        await Assert.ThrowsAsync<HttpRequestException>(() => SsrfSafeHttpHandler.ResolvePublicAddressesAsync(host, CancellationToken.None));
    }

    [Fact]
    public async Task RealRequestThroughTheHandler_ToLoopback_NeverConnects()
    {
        // End to end through the actual handler FlowEngine uses: refused before any socket opens.
        using var client = new HttpClient(SsrfSafeHttpHandler.Create());

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://localhost:5056/health"));
        Assert.Contains("манъ", ex.ToString());
    }
}
