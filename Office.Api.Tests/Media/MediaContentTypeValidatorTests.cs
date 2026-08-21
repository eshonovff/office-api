using Office.Api.Data.Entities;
using Office.Api.Media;

namespace Office.Api.Tests.Media;

public class MediaContentTypeValidatorTests
{
    [Theory]
    [InlineData(MessageType.Image, "image/jpeg", true)]
    [InlineData(MessageType.Image, "image/png", true)]
    [InlineData(MessageType.Video, "video/mp4", true)]
    [InlineData(MessageType.Audio, "audio/ogg", true)]
    [InlineData(MessageType.File, "application/pdf", true)]
    [InlineData(MessageType.File, "application/zip", true)]
    public void Matches_RealMediaContentType_ReturnsTrue(MessageType type, string contentType, bool expected)
    {
        Assert.Equal(expected, MediaContentTypeValidator.Matches(type, contentType));
    }

    // Regression: a 200 OK with Content-Type: text/html was silently accepted as a successful
    // download for two days — this is exactly the case that must never pass again.
    [Theory]
    [InlineData(MessageType.Image, "text/html")]
    [InlineData(MessageType.Video, "text/html")]
    [InlineData(MessageType.Audio, "text/html")]
    [InlineData(MessageType.File, "text/html")]
    public void Matches_HtmlContentType_ReturnsFalse(MessageType type, string contentType)
    {
        Assert.False(MediaContentTypeValidator.Matches(type, contentType));
    }

    [Theory]
    [InlineData(MessageType.Image, "video/mp4")]
    [InlineData(MessageType.Video, "image/jpeg")]
    [InlineData(MessageType.Audio, "application/json")]
    public void Matches_WrongMediaCategory_ReturnsFalse(MessageType type, string contentType)
    {
        Assert.False(MediaContentTypeValidator.Matches(type, contentType));
    }

    [Fact]
    public void Matches_MissingContentType_ReturnsFalse()
    {
        Assert.False(MediaContentTypeValidator.Matches(MessageType.Image, null));
        Assert.False(MediaContentTypeValidator.Matches(MessageType.Image, ""));
    }

    [Fact]
    public void Matches_ContentTypeWithCharsetSuffix_StillMatchesOnTheBaseType()
    {
        Assert.True(MediaContentTypeValidator.Matches(MessageType.File, "application/pdf; charset=binary"));
        Assert.False(MediaContentTypeValidator.Matches(MessageType.Image, "text/html; charset=utf-8"));
    }
}
