using Office.Api.Channels;

namespace Office.Api.Tests.Channels;

public class MediaLogRedactorTests
{
    [Fact]
    public void Redact_OpaqueWhatsAppStyleId_ReturnsUnchanged()
    {
        Assert.Equal("1234567890123456", MediaLogRedactor.Redact("1234567890123456"));
    }

    [Fact]
    public void Redact_FacebookCdnUrl_StripsQueryString()
    {
        var result = MediaLogRedactor.Redact("https://scontent.xx.fbcdn.net/v/t1/photo.jpg?_nc_ht=x&oe=ABC123&secret_token=XYZ");

        Assert.DoesNotContain("secret_token", result);
        Assert.DoesNotContain("ABC123", result);
        Assert.StartsWith("https://scontent.xx.fbcdn.net/v/t1/photo.jpg", result);
    }

    [Fact]
    public void Redact_UrlWithNoQueryString_StillSafe()
    {
        var result = MediaLogRedactor.Redact("https://scontent.cdninstagram.com/v/reel.mp4");

        Assert.Equal("https://scontent.cdninstagram.com/v/reel.mp4 (query бурида шуд)", result);
    }
}
