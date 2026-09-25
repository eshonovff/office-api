using Office.Api.Features.CustomerAuth;

namespace Office.Api.Tests.Features.CustomerAuth;

public class PasswordResetRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Token_Is256RandomBits_UrlSafe_AndStoredOnlyAsItsHash()
    {
        var (token, hash) = PasswordResetRules.NewToken();

        Assert.Equal(43, token.Length); // 32 bytes, base64url without padding
        Assert.Matches("^[A-Za-z0-9_-]+$", token);
        Assert.Matches("^[0-9A-F]{64}$", hash);
        Assert.NotEqual(token, hash);
        Assert.Equal(hash, PasswordResetRules.Hash(token));
    }

    [Fact]
    public void Tokens_NeverRepeat()
    {
        var tokens = Enumerable.Range(0, 1000).Select(_ => PasswordResetRules.NewToken().Token).ToHashSet();
        Assert.Equal(1000, tokens.Count);
    }

    [Fact]
    public void FirstLink_IsSent()
    {
        Assert.Equal(PasswordResetSendResult.Sent, PasswordResetRules.CanSend([], Now));
    }

    [Fact]
    public void NotTwiceWithinAMinute()
    {
        Assert.Equal(PasswordResetSendResult.CoolingDown, PasswordResetRules.CanSend([Now.AddSeconds(-59)], Now));
        Assert.Equal(PasswordResetSendResult.Sent, PasswordResetRules.CanSend([Now.AddSeconds(-61)], Now));
    }

    [Fact]
    public void AtMostFiveADay()
    {
        var five = Enumerable.Range(1, 5).Select(h => Now.AddHours(-h)).ToList();
        Assert.Equal(PasswordResetSendResult.DailyCapReached, PasswordResetRules.CanSend(five, Now));

        var fourAndOneOld = Enumerable.Range(1, 4).Select(h => Now.AddHours(-h)).Append(Now.AddHours(-25)).ToList();
        Assert.Equal(PasswordResetSendResult.Sent, PasswordResetRules.CanSend(fourAndOneOld, Now));
    }
}
