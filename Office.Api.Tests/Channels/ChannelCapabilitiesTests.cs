using Office.Api.Channels;
using Office.Api.Data.Entities;

namespace Office.Api.Tests.Channels;

public class ChannelCapabilitiesTests
{
    [Fact]
    public void CanSendMedia_WhatsApp_ReturnsTrue()
    {
        Assert.True(ChannelCapabilities.CanSendMedia(ChannelType.WhatsApp));
    }

    [Theory]
    [InlineData(ChannelType.Instagram)]
    [InlineData(ChannelType.Facebook)]
    public void CanSendMedia_MessengerChannels_ReturnsFalse(ChannelType channelType)
    {
        // Instagram: confirmed live 2026-08-25 (message_attachments 200s, POST /messages with
        // attachment_id always 500s until App Review). Facebook: NOT confirmed broken — set
        // false anyway per explicit owner instruction, see the TODO on ChannelCapabilities.
        Assert.False(ChannelCapabilities.CanSendMedia(channelType));
    }

    [Fact]
    public void CanSendVoice_WhatsApp_ReturnsTrue()
    {
        Assert.True(ChannelCapabilities.CanSendVoice(ChannelType.WhatsApp));
    }

    [Theory]
    [InlineData(ChannelType.Instagram)]
    [InlineData(ChannelType.Facebook)]
    public void CanSendVoice_MessengerChannels_ReturnsFalse(ChannelType channelType)
    {
        Assert.False(ChannelCapabilities.CanSendVoice(channelType));
    }
}
