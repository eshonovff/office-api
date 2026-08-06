using System.Security.Cryptography;
using System.Text;
using Office.Api.Channels;

namespace Office.Api.Tests.Channels;

public class WebhookSignatureTests
{
    private const string AppSecret = "test-app-secret";

    private static string ComputeValidHeader(string body, string secret = AppSecret)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body));
        return $"sha256={Convert.ToHexString(hash)}";
    }

    [Fact]
    public void IsValid_CorrectSignature_ReturnsTrue()
    {
        const string body = """{"messages":[]}""";
        var header = ComputeValidHeader(body);

        Assert.True(WebhookSignature.IsValid(body, header, AppSecret));
    }

    [Fact]
    public void IsValid_MissingHeader_ReturnsFalse()
    {
        Assert.False(WebhookSignature.IsValid("{}", null, AppSecret));
    }

    [Fact]
    public void IsValid_WrongSecret_ReturnsFalse()
    {
        const string body = """{"messages":[]}""";
        var header = ComputeValidHeader(body, "different-secret");

        Assert.False(WebhookSignature.IsValid(body, header, AppSecret));
    }

    [Fact]
    public void IsValid_TamperedBody_ReturnsFalse()
    {
        var header = ComputeValidHeader("""{"messages":[]}""");

        Assert.False(WebhookSignature.IsValid("""{"messages":[{"injected":true}]}""", header, AppSecret));
    }

    [Fact]
    public void IsValid_MissingShaPrefix_ReturnsFalse()
    {
        Assert.False(WebhookSignature.IsValid("{}", "not-a-real-signature", AppSecret));
    }

    [Fact]
    public void IsValid_MalformedHex_ReturnsFalse()
    {
        Assert.False(WebhookSignature.IsValid("{}", "sha256=not-hex-zzz", AppSecret));
    }
}
