using System.Security.Cryptography;
using System.Text;
using Office.Api.Features.DataDeletion;

namespace Office.Api.Tests.Features.DataDeletion;

public class MetaSignedRequestTests
{
    private const string Secret = "app-secret";
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private static string B64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Builds a signed_request exactly the way Meta does.</summary>
    private static string Sign(string payloadJson, string secret = Secret)
    {
        var payload = B64Url(Encoding.UTF8.GetBytes(payloadJson));
        var signature = B64Url(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload)));
        return $"{signature}.{payload}";
    }

    private static string Payload(string userId = "\"27208597255483913\"", long? issuedAt = null, string algorithm = "HMAC-SHA256") =>
        $$"""{"algorithm":"{{algorithm}}","user_id":{{userId}},"issued_at":{{issuedAt ?? Now.ToUnixTimeSeconds()}}}""";

    [Fact]
    public void ValidRequest_ReturnsTheUser()
    {
        var result = MetaSignedRequest.TryVerify(Sign(Payload()), Secret, Now);

        Assert.NotNull(result);
        Assert.Equal("27208597255483913", result.UserId);
    }

    [Fact]
    public void NumericUserId_IsAccepted()
    {
        Assert.Equal("123", MetaSignedRequest.TryVerify(Sign(Payload(userId: "123")), Secret, Now)?.UserId);
    }

    [Fact]
    public void SignedWithAnotherSecret_IsRefused()
    {
        Assert.Null(MetaSignedRequest.TryVerify(Sign(Payload(), secret: "attacker-guess"), Secret, Now));
    }

    [Fact]
    public void TamperedPayload_IsRefused()
    {
        // Valid signature of one user, payload swapped for another — the classic forgery.
        var genuine = Sign(Payload(userId: "\"victim-of-nobody\""));
        var forgedPayload = B64Url(Encoding.UTF8.GetBytes(Payload(userId: "\"victim\"")));
        var forged = genuine.Split('.')[0] + "." + forgedPayload;

        Assert.Null(MetaSignedRequest.TryVerify(forged, Secret, Now));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("HMAC-SHA1")]
    public void OtherAlgorithm_IsRefused(string algorithm)
    {
        Assert.Null(MetaSignedRequest.TryVerify(Sign(Payload(algorithm: algorithm)), Secret, Now));
    }

    [Fact]
    public void OlderThanADay_IsRefused()
    {
        var old = Now.AddHours(-25).ToUnixTimeSeconds();
        Assert.Null(MetaSignedRequest.TryVerify(Sign(Payload(issuedAt: old)), Secret, Now));
    }

    [Fact]
    public void FromTheFuture_IsRefused()
    {
        var future = Now.AddMinutes(10).ToUnixTimeSeconds();
        Assert.Null(MetaSignedRequest.TryVerify(Sign(Payload(issuedAt: future)), Secret, Now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-dot")]
    [InlineData("a.b.c")]
    [InlineData(".payload")]
    [InlineData("sig.")]
    [InlineData("!!!.@@@")]
    public void Malformed_IsRefused(string? signedRequest)
    {
        Assert.Null(MetaSignedRequest.TryVerify(signedRequest, Secret, Now));
    }

    [Fact]
    public void MissingUserId_IsRefused()
    {
        var json = $$"""{"algorithm":"HMAC-SHA256","issued_at":{{Now.ToUnixTimeSeconds()}}}""";
        Assert.Null(MetaSignedRequest.TryVerify(Sign(json), Secret, Now));
    }

    [Fact]
    public void EmptySecretConfigured_RefusesEverything()
    {
        // A missing app secret must never turn into "accept anything signed with an empty key".
        Assert.Null(MetaSignedRequest.TryVerify(Sign(Payload(), secret: ""), "", Now));
    }
}
