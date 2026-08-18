namespace Office.Api.Channels.Meta;

/// <summary>
/// Хониши конфигуратсияи Meta OAuth — як App (App Id/Secret) барои ҳарду маҳсулот
/// (Facebook Login for Business ва Instagram API with Instagram Login якҷоя дар
/// консоли Meta илова мешаванд). RedirectBaseUrl байни dev (tunnel) ва prod фарқ мекунад.
/// </summary>
internal static class MetaOAuthConfig
{
    public static string GetAppId(IConfiguration configuration) =>
        configuration["Meta:AppId"] ?? throw new InvalidOperationException("Meta:AppId танзим нашудааст.");

    public static string GetAppSecret(IConfiguration configuration) =>
        configuration["Meta:AppSecret"] ?? throw new InvalidOperationException("Meta:AppSecret танзим нашудааст.");

    public static string GetRedirectBaseUrl(IConfiguration configuration) =>
        configuration["Meta:RedirectBaseUrl"]?.TrimEnd('/')
        ?? throw new InvalidOperationException("Meta:RedirectBaseUrl танзим нашудааст.");
}
