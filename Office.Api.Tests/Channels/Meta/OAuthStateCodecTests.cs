using Office.Api.Channels.Meta;

namespace Office.Api.Tests.Channels.Meta;

public class OAuthStateCodecTests
{
    private const string SigningKey = "test-signing-key";
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryDecode_RoundTrip_ReturnsSamePayload()
    {
        var expiresAt = Now.AddMinutes(10);
        var token = OAuthStateCodec.Encode("facebook", UserId, "nonce-1", expiresAt, SigningKey);

        var ok = OAuthStateCodec.TryDecode(token, SigningKey, Now, out var payload);

        Assert.True(ok);
        Assert.Equal("facebook", payload!.Provider);
        Assert.Equal(UserId, payload.UserId);
        Assert.Equal("nonce-1", payload.Nonce);
        Assert.Equal(expiresAt.ToUnixTimeSeconds(), payload.ExpiresAtUnix);
    }

    [Fact]
    public void TryDecode_Expired_ReturnsFalse()
    {
        var token = OAuthStateCodec.Encode("instagram", UserId, "nonce-1", Now.AddMinutes(-1), SigningKey);

        var ok = OAuthStateCodec.TryDecode(token, SigningKey, Now, out var payload);

        Assert.False(ok);
        Assert.Null(payload);
    }

    [Fact]
    public void TryDecode_ExactlyAtExpiry_ReturnsTrue()
    {
        var expiresAt = Now;
        var token = OAuthStateCodec.Encode("instagram", UserId, "nonce-1", expiresAt, SigningKey);

        var ok = OAuthStateCodec.TryDecode(token, SigningKey, Now, out _);

        Assert.True(ok);
    }

    [Fact]
    public void TryDecode_WrongSigningKey_ReturnsFalse()
    {
        var token = OAuthStateCodec.Encode("facebook", UserId, "nonce-1", Now.AddMinutes(10), SigningKey);

        var ok = OAuthStateCodec.TryDecode(token, "a-different-key", Now, out var payload);

        Assert.False(ok);
        Assert.Null(payload);
    }

    [Fact]
    public void TryDecode_TamperedPayload_ReturnsFalse()
    {
        var token = OAuthStateCodec.Encode("facebook", UserId, "nonce-1", Now.AddMinutes(10), SigningKey);
        var separatorIndex = token.IndexOf('.');
        var tampered = "different-payload" + token[separatorIndex..];

        var ok = OAuthStateCodec.TryDecode(tampered, SigningKey, Now, out var payload);

        Assert.False(ok);
        Assert.Null(payload);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-dot-here")]
    [InlineData(".missing-payload")]
    [InlineData("missing-signature.")]
    [InlineData("not-base64!!!.deadbeef")]
    [InlineData("cGF5bG9hZA==.not-hex-zzz")]
    public void TryDecode_MalformedToken_ReturnsFalse(string token)
    {
        var ok = OAuthStateCodec.TryDecode(token, SigningKey, Now, out var payload);

        Assert.False(ok);
        Assert.Null(payload);
    }
}
