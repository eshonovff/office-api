using Office.Api.Media;

namespace Office.Api.Tests.Media;

public class MediaContentTypeValidatorTests
{
    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("video/mp4")]
    [InlineData("audio/ogg")]
    [InlineData("application/pdf")]
    [InlineData("application/zip")]
    [InlineData("application/octet-stream")]
    // Instagram wraps a voice message in an mp4 container more often than not (not always
    // audio/*) — this used to be rejected as a "wrong category" even though the file is
    // completely healthy. The validator no longer cares which category a message expected.
    [InlineData("video/quicktime")]
    [InlineData("application/ogg")]
    public void Matches_RealMediaContentType_ReturnsTrue(string contentType)
    {
        Assert.True(MediaContentTypeValidator.Matches(contentType));
    }

    // Regression: a 200 OK with Content-Type: text/html was silently accepted as a successful
    // download for two days — this is exactly the case that must never pass again.
    [Theory]
    [InlineData("text/html")]
    [InlineData("text/html; charset=utf-8")]
    [InlineData("text/plain")]
    [InlineData("application/json")]
    [InlineData("application/problem+json")]
    [InlineData("application/xml")]
    public void Matches_ErrorPageOrApiResponseContentType_ReturnsFalse(string contentType)
    {
        Assert.False(MediaContentTypeValidator.Matches(contentType));
    }

    [Fact]
    public void Matches_MissingContentType_ReturnsFalse()
    {
        Assert.False(MediaContentTypeValidator.Matches(null));
        Assert.False(MediaContentTypeValidator.Matches(""));
    }

    [Fact]
    public void Matches_ContentTypeWithCharsetSuffix_StillMatchesOnTheBaseType()
    {
        Assert.True(MediaContentTypeValidator.Matches("application/pdf; charset=binary"));
    }
}
