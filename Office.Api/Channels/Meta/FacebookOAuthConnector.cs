namespace Office.Api.Channels.Meta;

/// <summary>Facebook Login for Business — барои пайвасти Page (пеш аз фазаи 7-и воқеӣ, танҳо сохтани канал).</summary>
public class FacebookOAuthConnector(IConfiguration configuration) : IChannelOAuthConnector
{
    private const string GraphApiVersion = "v23.0";

    // pages_show_list: рӯйхати Page-ҳо; pages_messaging: фиристодан/қабули паём;
    // pages_manage_metadata: обуна ба webhook-и messages/messaging_postbacks дар вақти connect.
    private const string Scopes = "pages_show_list,pages_messaging,pages_manage_metadata";

    public string BuildAuthorizationUrl(string redirectUri, string state)
    {
        var appId = MetaOAuthConfig.GetAppId(configuration);
        return $"https://www.facebook.com/{GraphApiVersion}/dialog/oauth" +
               $"?client_id={Uri.EscapeDataString(appId)}" +
               $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
               $"&state={Uri.EscapeDataString(state)}" +
               $"&scope={Scopes}" +
               "&response_type=code";
    }
}
