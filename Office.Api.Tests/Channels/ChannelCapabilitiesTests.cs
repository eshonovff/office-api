using Office.Api.Channels;
using Office.Api.Data.Entities;

namespace Office.Api.Tests.Channels;

public class ChannelCapabilitiesTests
{
    [Theory]
    [InlineData(ChannelType.WhatsApp)]
    [InlineData(ChannelType.Facebook)]
    [InlineData(ChannelType.Instagram)]
    public void CanSendMedia_EveryChannel_ReturnsTrue(ChannelType channelType)
    {
        // Facebook: confirmed live 2026-08-26. Instagram: confirmed live 2026-09-26 once uploads
        // were typed — image, video, voice (m4a) and PDF all delivered (see ChannelCapabilities).
        Assert.True(ChannelCapabilities.CanSendMedia(channelType));
    }

    [Theory]
    [InlineData(ChannelType.WhatsApp)]
    [InlineData(ChannelType.Facebook)]
    [InlineData(ChannelType.Instagram)]
    public void CanSendVoice_EveryChannel_ReturnsTrue(ChannelType channelType)
    {
        Assert.True(ChannelCapabilities.CanSendVoice(channelType));
    }
}
