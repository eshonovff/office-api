using Office.Api.Data.Entities;

namespace Office.Api.Channels.Meta;

public interface IChannelOAuthConnectorFactory
{
    IChannelOAuthConnector GetConnector(ChannelType type);
}

public class ChannelOAuthConnectorFactory(IServiceProvider serviceProvider) : IChannelOAuthConnectorFactory
{
    public IChannelOAuthConnector GetConnector(ChannelType type) => type switch
    {
        ChannelType.Facebook => serviceProvider.GetRequiredService<FacebookOAuthConnector>(),
        ChannelType.Instagram => serviceProvider.GetRequiredService<InstagramOAuthConnector>(),
        _ => throw new NotSupportedException(
            $"OAuth-и '{type}' дастгирӣ намешавад — WhatsApp тавассути credentials дастӣ пайваст мешавад."),
    };
}
