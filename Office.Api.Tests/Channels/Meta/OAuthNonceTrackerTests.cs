using Office.Api.Channels.Meta;

namespace Office.Api.Tests.Channels.Meta;

public class OAuthNonceTrackerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryConsume_FirstUse_ReturnsTrue()
    {
        var tracker = new OAuthNonceTracker();

        Assert.True(tracker.TryConsume("nonce-1", Now.AddMinutes(10), Now));
    }

    [Fact]
    public void TryConsume_Replay_ReturnsFalse()
    {
        var tracker = new OAuthNonceTracker();
        tracker.TryConsume("nonce-1", Now.AddMinutes(10), Now);

        var replayed = tracker.TryConsume("nonce-1", Now.AddMinutes(10), Now.AddSeconds(1));

        Assert.False(replayed);
    }

    [Fact]
    public void TryConsume_DifferentNonces_BothSucceed()
    {
        var tracker = new OAuthNonceTracker();

        Assert.True(tracker.TryConsume("nonce-1", Now.AddMinutes(10), Now));
        Assert.True(tracker.TryConsume("nonce-2", Now.AddMinutes(10), Now));
    }

    [Fact]
    public void TryConsume_AfterOwnExpiry_CanBeConsumedAgain()
    {
        var tracker = new OAuthNonceTracker();
        var expiresAt = Now.AddMinutes(10);
        tracker.TryConsume("nonce-1", expiresAt, Now);

        var afterExpiry = tracker.TryConsume("nonce-1", Now.AddMinutes(20), expiresAt.AddSeconds(1));

        Assert.True(afterExpiry);
    }
}
