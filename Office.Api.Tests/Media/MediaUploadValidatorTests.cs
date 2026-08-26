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
    public void Classify_WhatsApp_ReturnsExpectedTypeAndLimit(string mimeType, MessageType expectedType, long expectedMaxBytes)
    {
        var (type, maxSizeBytes) = MediaUploadValidator.Classify(ChannelType.WhatsApp, mimeType);

        Assert.Equal(expectedType, type);
        Assert.Equal(expectedMaxBytes, maxSizeBytes);
    }

    [Fact]
    public void IsWithinLimit_WhatsAppImageAtExactly5MB_ReturnsTrue()
    {
        Assert.True(MediaUploadValidator.IsWithinLimit(ChannelType.WhatsApp, "image/jpeg", MediaUploadValidator.ImageMaxBytes));
    }

    [Fact]
    public void IsWithinLimit_WhatsAppImageOneByteOver5MB_ReturnsFalse()
    {
        Assert.False(MediaUploadValidator.IsWithinLimit(ChannelType.WhatsApp, "image/jpeg", MediaUploadValidator.ImageMaxBytes + 1));
    }

    [Fact]
    public void IsWithinLimit_WhatsAppDocumentAt100MB_ReturnsTrue()
    {
        Assert.True(MediaUploadValidator.IsWithinLimit(ChannelType.WhatsApp, "application/pdf", MediaUploadValidator.DocumentMaxBytes));
    }

    [Fact]
    public void IsWithinLimit_WhatsAppVideoOver16MB_ReturnsFalse()
    {
        Assert.False(MediaUploadValidator.IsWithinLimit(ChannelType.WhatsApp, "video/mp4", MediaUploadValidator.AudioVideoMaxBytes + 1));
    }

    [Fact]
    public void IsWithinLimit_ZeroBytes_ReturnsFalse()
    {
        Assert.False(MediaUploadValidator.IsWithinLimit(ChannelType.WhatsApp, "image/jpeg", 0));
    }

    [Theory]
    [InlineData(ChannelType.Instagram, "image/jpeg", MessageType.Image)]
    [InlineData(ChannelType.Instagram, "video/mp4", MessageType.Video)]
    [InlineData(ChannelType.Instagram, "application/pdf", MessageType.File)]
    [InlineData(ChannelType.Facebook, "image/jpeg", MessageType.Image)]
    [InlineData(ChannelType.Facebook, "video/mp4", MessageType.Video)]
    [InlineData(ChannelType.Facebook, "application/pdf", MessageType.File)]
    public void Classify_MessengerChannels_UseTheSameFlatLimitRegardlessOfCategory(ChannelType channelType, string mimeType, MessageType expectedType)
    {
        var (type, maxSizeBytes) = MediaUploadValidator.Classify(channelType, mimeType);

        Assert.Equal(expectedType, type);
        Assert.Equal(MediaUploadValidator.MessengerAttachmentMaxBytes, maxSizeBytes);
    }

    [Theory]
    [InlineData(ChannelType.Instagram)]
    [InlineData(ChannelType.Facebook)]
    public void IsWithinLimit_MessengerImageAt20MB_ReturnsTrue_EvenThoughItWouldFailOnWhatsApp(ChannelType channelType)
    {
        const long twentyMb = 20 * 1024 * 1024;

        Assert.True(MediaUploadValidator.IsWithinLimit(channelType, "image/jpeg", twentyMb));
        Assert.False(MediaUploadValidator.IsWithinLimit(ChannelType.WhatsApp, "image/jpeg", twentyMb));
    }

    [Theory]
    [InlineData(ChannelType.Instagram)]
    [InlineData(ChannelType.Facebook)]
    public void IsWithinLimit_MessengerOneByteOver25MB_ReturnsFalse(ChannelType channelType)
    {
        Assert.False(MediaUploadValidator.IsWithinLimit(channelType, "video/mp4", MediaUploadValidator.MessengerAttachmentMaxBytes + 1));
    }

    [Fact]
    public void LimitsFor_WhatsApp_ReturnsTheThreeDistinctCategoryLimits()
    {
        var limits = MediaUploadValidator.LimitsFor(ChannelType.WhatsApp);

        Assert.Equal(
            new[]
            {
                new MediaTypeLimit("image", MediaUploadValidator.ImageMaxBytes),
                new MediaTypeLimit("audioVideo", MediaUploadValidator.AudioVideoMaxBytes),
                new MediaTypeLimit("document", MediaUploadValidator.DocumentMaxBytes),
            },
            limits);
    }

    [Theory]
    [InlineData(ChannelType.Instagram)]
    [InlineData(ChannelType.Facebook)]
    public void LimitsFor_MessengerChannels_AllThreeCategoriesShareTheSameFlatLimit(ChannelType channelType)
    {
        var limits = MediaUploadValidator.LimitsFor(channelType);

        Assert.All(limits, limit => Assert.Equal(MediaUploadValidator.MessengerAttachmentMaxBytes, limit.MaxSizeBytes));
        Assert.Equal(["image", "audioVideo", "document"], limits.Select(l => l.Category));
    }
}
