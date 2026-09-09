using Office.Api.Channels;
using Office.Api.Data.Entities;

namespace Office.Api.Tests.Channels;

public class ChannelCapabilitiesTests
{
    [Theory]
    [InlineData(ChannelType.WhatsApp)]
    [InlineData(ChannelType.Facebook)]
    public void CanSendMedia_WhatsAppAndFacebook_ReturnsTrue(ChannelType channelType)
    {
        // Facebook: тасдиқшуда зинда 2026-08-26 — ниг. ChannelCapabilities барои далел
        // (5 такрори мустақил, ҳама 200).
        Assert.True(ChannelCapabilities.CanSendMedia(channelType));
    }

    [Fact]
    public void CanSendMedia_Instagram_ReturnsFalse()
    {
        // Instagram: confirmed live 2026-08-25 (message_attachments 200s, POST /messages with
        // attachment_id always 500s until App Review).
        Assert.False(ChannelCapabilities.CanSendMedia(ChannelType.Instagram));
    }

    [Theory]
    [InlineData(ChannelType.WhatsApp)]
    [InlineData(ChannelType.Facebook)]
    public void CanSendVoice_WhatsAppAndFacebook_ReturnsTrue(ChannelType channelType)
    {
        Assert.True(ChannelCapabilities.CanSendVoice(channelType));
    }

    [Fact]
    public void CanSendVoice_Instagram_ReturnsFalse()
    {
        Assert.False(ChannelCapabilities.CanSendVoice(ChannelType.Instagram));
    }
}
