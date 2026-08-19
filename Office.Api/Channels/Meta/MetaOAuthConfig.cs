using Office.Api.Data.Entities;

namespace Office.Api.Channels.Meta;

/// <summary>
/// Хониши конфигуратсияи Meta OAuth — ҲАР provider App Id/Secret-и худро дорад.
/// Facebook Login for Business App-и асосии Meta-ро истифода мебарад, вале Instagram
/// API with Instagram Login App-и АЛОҲИДА аст (ID/Secret-и худ, аз app-и асосӣ фарқ
/// мекунад) — як ҷуфти умумӣ дар ин ҷо кор намекунад. RedirectBaseUrl байни dev
/// (tunnel) ва prod фарқ мекунад, вале байни provider-ҳо як аст.
/// </summary>
internal static class MetaOAuthConfig
{
    public static string GetAppId(IConfiguration configuration, ChannelType provider) =>
        configuration[$"Meta:{ProviderKey(provider)}:AppId"]
        ?? throw new InvalidOperationException($"Meta:{ProviderKey(provider)}:AppId танзим нашудааст.");

    public static string GetAppSecret(IConfiguration configuration, ChannelType provider) =>
        configuration[$"Meta:{ProviderKey(provider)}:AppSecret"]
        ?? throw new InvalidOperationException($"Meta:{ProviderKey(provider)}:AppSecret танзим нашудааст.");

    public static string GetRedirectBaseUrl(IConfiguration configuration) =>
        configuration["Meta:RedirectBaseUrl"]?.TrimEnd('/')
        ?? throw new InvalidOperationException("Meta:RedirectBaseUrl танзим нашудааст.");

    private static string ProviderKey(ChannelType provider) => provider switch
    {
        ChannelType.Facebook => "Facebook",
        ChannelType.Instagram => "Instagram",
        _ => throw new NotSupportedException($"Meta OAuth-и '{provider}' дастгирӣ намешавад."),
    };
}
