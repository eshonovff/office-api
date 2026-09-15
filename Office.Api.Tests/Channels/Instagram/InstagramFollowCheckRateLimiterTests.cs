using Office.Api.Channels.Instagram;

namespace Office.Api.Tests.Channels.Instagram;

public class InstagramFollowCheckRateLimiterTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryConsume_UnderLimit_ReturnsTrue()
    {
        var limiter = new InstagramFollowCheckRateLimiter();

        Assert.True(limiter.TryConsume(limit: 3, Start));
        Assert.True(limiter.TryConsume(limit: 3, Start.AddSeconds(1)));
        Assert.True(limiter.TryConsume(limit: 3, Start.AddSeconds(2)));
    }

    [Fact]
    public void TryConsume_AtLimit_ReturnsFalse()
    {
        var limiter = new InstagramFollowCheckRateLimiter();
        limiter.TryConsume(limit: 2, Start);
        limiter.TryConsume(limit: 2, Start.AddSeconds(1));

        Assert.False(limiter.TryConsume(limit: 2, Start.AddSeconds(2)));
    }

    [Fact]
    public void TryConsume_AfterOneHour_WindowResetsAndAllowsAgain()
    {
        var limiter = new InstagramFollowCheckRateLimiter();
        limiter.TryConsume(limit: 1, Start);
        Assert.False(limiter.TryConsume(limit: 1, Start.AddMinutes(59)));

        Assert.True(limiter.TryConsume(limit: 1, Start.AddHours(1)));
    }

    [Fact]
    public void TryConsume_JustBeforeOneHour_StillCountsAgainstOldWindow()
    {
        var limiter = new InstagramFollowCheckRateLimiter();
        limiter.TryConsume(limit: 1, Start);

        Assert.False(limiter.TryConsume(limit: 1, Start.AddMinutes(59).AddSeconds(59)));
    }

    [Fact]
    public void TryConsume_ZeroLimit_AlwaysReturnsFalse()
    {
        var limiter = new InstagramFollowCheckRateLimiter();

        Assert.False(limiter.TryConsume(limit: 0, Start));
    }
}
