using Office.Api.Channels;
using Office.Api.Data.Entities;

namespace Office.Api.Tests.Channels;

public class WebhookAppSecretSelectorTests
{
    [Fact]
    public void UseInstagramAppSecret_Instagram_ReturnsTrue()
    {
        Assert.True(WebhookAppSecretSelector.UseInstagramAppSecret(ChannelType.Instagram));
    }

    [Fact]
    public void UseInstagramAppSecret_Facebook_ReturnsFalse()
    {
        Assert.False(WebhookAppSecretSelector.UseInstagramAppSecret(ChannelType.Facebook));
    }

    [Fact]
    public void UseInstagramAppSecret_WhatsApp_ReturnsFalse()
    {
        Assert.False(WebhookAppSecretSelector.UseInstagramAppSecret(ChannelType.WhatsApp));
    }
}
