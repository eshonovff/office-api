using Office.Api.Channels.Facebook;
using Office.Api.Channels.Instagram;
using Office.Api.Channels.WhatsApp;
using Office.Api.Data.Entities;

namespace Office.Api.Channels;

public interface IChannelProviderFactory
{
    IChannelProvider GetProvider(ChannelType type);
}

public class ChannelProviderFactory(IServiceProvider serviceProvider) : IChannelProviderFactory
{
    public IChannelProvider GetProvider(ChannelType type) => type switch
    {
        ChannelType.WhatsApp => serviceProvider.GetRequiredService<WhatsAppProvider>(),
        ChannelType.Facebook => serviceProvider.GetRequiredService<FacebookProvider>(),
        ChannelType.Instagram => serviceProvider.GetRequiredService<InstagramProvider>(),

        _ => throw new NotSupportedException($"Навъи канали '{type}' дастгирӣ намешавад."),
    };
}
