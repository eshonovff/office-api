using Office.Api.Channels.Instagram;

namespace Office.Api.Tests.Channels.Instagram;

public class InstagramTokenRefreshPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ShouldRefresh_NoExpiryStored_ReturnsFalse()
    {
        Assert.False(InstagramTokenRefreshPolicy.ShouldRefresh(null, Now));
    }

    [Fact]
    public void ShouldRefresh_FarFromExpiry_ReturnsFalse()
    {
        Assert.False(InstagramTokenRefreshPolicy.ShouldRefresh(Now.AddDays(30), Now));
    }

    [Fact]
    public void ShouldRefresh_WithinTheWindow_ReturnsTrue()
    {
        Assert.True(InstagramTokenRefreshPolicy.ShouldRefresh(Now.AddDays(5), Now));
    }

    [Fact]
    public void ShouldRefresh_ExactlyAtTheWindowBoundary_ReturnsTrue()
    {
        Assert.True(InstagramTokenRefreshPolicy.ShouldRefresh(Now + InstagramTokenRefreshPolicy.RefreshWindow, Now));
    }

    [Fact]
    public void ShouldRefresh_AlreadyExpired_StillReturnsTrue()
    {
        // A refresh attempt is still worth trying even past expiry — ig_refresh_token itself will
        // reject it if it's truly too late, and IsPastExpiry is what decides RequiresReconnect.
        Assert.True(InstagramTokenRefreshPolicy.ShouldRefresh(Now.AddDays(-1), Now));
    }

    [Fact]
    public void IsPastExpiry_FutureExpiry_ReturnsFalse()
    {
        Assert.False(InstagramTokenRefreshPolicy.IsPastExpiry(Now.AddDays(1), Now));
    }

    [Fact]
    public void IsPastExpiry_AlreadyPast_ReturnsTrue()
    {
        Assert.True(InstagramTokenRefreshPolicy.IsPastExpiry(Now.AddDays(-1), Now));
    }

    [Fact]
    public void IsPastExpiry_NoExpiryStored_ReturnsFalse()
    {
        Assert.False(InstagramTokenRefreshPolicy.IsPastExpiry(null, Now));
    }
}
