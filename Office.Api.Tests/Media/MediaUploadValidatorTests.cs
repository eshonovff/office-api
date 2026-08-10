using Office.Api.Data.Entities;
using Office.Api.Media;

namespace Office.Api.Tests.Media;

public class MediaUploadValidatorTests
{
    [Theory]
    [InlineData("image/jpeg", MessageType.Image, MediaUploadValidator.ImageMaxBytes)]
    [InlineData("image/png", MessageType.Image, MediaUploadValidator.ImageMaxBytes)]
    [InlineData("video/mp4", MessageType.Video, MediaUploadValidator.AudioVideoMaxBytes)]
    [InlineData("audio/ogg", MessageType.Audio, MediaUploadValidator.AudioVideoMaxBytes)]
    [InlineData("application/pdf", MessageType.File, MediaUploadValidator.DocumentMaxBytes)]
    [InlineData("application/octet-stream", MessageType.File, MediaUploadValidator.DocumentMaxBytes)]
    public void Classify_ReturnsExpectedTypeAndLimit(string mimeType, MessageType expectedType, long expectedMaxBytes)
    {
        var (type, maxSizeBytes) = MediaUploadValidator.Classify(mimeType);

        Assert.Equal(expectedType, type);
        Assert.Equal(expectedMaxBytes, maxSizeBytes);
    }

    [Fact]
    public void IsWithinLimit_ImageAtExactly5MB_ReturnsTrue()
    {
        Assert.True(MediaUploadValidator.IsWithinLimit("image/jpeg", MediaUploadValidator.ImageMaxBytes));
    }

    [Fact]
    public void IsWithinLimit_ImageOneByteOver5MB_ReturnsFalse()
    {
        Assert.False(MediaUploadValidator.IsWithinLimit("image/jpeg", MediaUploadValidator.ImageMaxBytes + 1));
    }

    [Fact]
    public void IsWithinLimit_DocumentAt100MB_ReturnsTrue()
    {
        Assert.True(MediaUploadValidator.IsWithinLimit("application/pdf", MediaUploadValidator.DocumentMaxBytes));
    }

    [Fact]
    public void IsWithinLimit_VideoOver16MB_ReturnsFalse()
    {
        Assert.False(MediaUploadValidator.IsWithinLimit("video/mp4", MediaUploadValidator.AudioVideoMaxBytes + 1));
    }

    [Fact]
    public void IsWithinLimit_ZeroBytes_ReturnsFalse()
    {
        Assert.False(MediaUploadValidator.IsWithinLimit("image/jpeg", 0));
    }
}
