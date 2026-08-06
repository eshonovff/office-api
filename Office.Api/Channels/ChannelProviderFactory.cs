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
        // TODO(фазаи 5): WhatsApp — амалисозии воқеӣ ба ҷои Placeholder.
        ChannelType.WhatsApp => serviceProvider.GetRequiredService<PlaceholderChannelProvider>(),

        // TODO(фазаи 7): Instagram/Facebook — амалисозии воқеӣ ба ҷои Placeholder.
        ChannelType.Instagram => serviceProvider.GetRequiredService<PlaceholderChannelProvider>(),
        ChannelType.Facebook => serviceProvider.GetRequiredService<PlaceholderChannelProvider>(),

        _ => throw new NotSupportedException($"Навъи канали '{type}' дастгирӣ намешавад."),
    };
}
