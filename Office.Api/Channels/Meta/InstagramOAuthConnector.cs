namespace Office.Api.Channels.Meta;

/// <summary>
/// Instagram API with Instagram Login — Facebook Page лозим нест, корбар мустақим бо
/// account-и бизнеси Instagram ворид мешавад.
/// </summary>
public class InstagramOAuthConnector(IConfiguration configuration) : IChannelOAuthConnector
{
    // instagram_business_basic: маълумоти профил; instagram_business_manage_messages: паёмҳои DM;
    // instagram_business_manage_comments: коментҳо (талаби корбар).
    private const string Scopes =
        "instagram_business_basic,instagram_business_manage_messages,instagram_business_manage_comments";

    public string BuildAuthorizationUrl(string redirectUri, string state)
    {
        var appId = MetaOAuthConfig.GetAppId(configuration);
        return "https://www.instagram.com/oauth/authorize" +
               $"?client_id={Uri.EscapeDataString(appId)}" +
               $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
               "&response_type=code" +
               $"&scope={Scopes}" +
               $"&state={Uri.EscapeDataString(state)}";
    }
}
