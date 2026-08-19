using System.Text.Json;
using Office.Api.Channels.Instagram;
using Office.Api.Data.Entities;

namespace Office.Api.Channels.Meta;

/// <summary>
/// Instagram API with Instagram Login — Facebook Page лозим нест, корбар мустақим бо
/// account-и бизнеси Instagram ворид мешавад.
/// </summary>
public class InstagramOAuthConnector(HttpClient httpClient, IConfiguration configuration, ILogger<InstagramOAuthConnector> logger)
    : IChannelOAuthConnector
{
    private const string GraphApiVersion = "v23.0";

    // instagram_business_basic: маълумоти профил; instagram_business_manage_messages: паёмҳои DM;
    // instagram_business_manage_comments: коментҳо (талаби корбар).
    private const string Scopes =
        "instagram_business_basic,instagram_business_manage_messages,instagram_business_manage_comments";

    public string BuildAuthorizationUrl(string redirectUri, string state)
    {
        var appId = MetaOAuthConfig.GetAppId(configuration, ChannelType.Instagram);
        return "https://www.instagram.com/oauth/authorize" +
               $"?client_id={Uri.EscapeDataString(appId)}" +
               $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
               "&response_type=code" +
               $"&scope={Scopes}" +
               $"&state={Uri.EscapeDataString(state)}";
    }

    public async Task<IReadOnlyList<ConnectableAccount>> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct)
    {
        var appId = MetaOAuthConfig.GetAppId(configuration, ChannelType.Instagram);
        var appSecret = MetaOAuthConfig.GetAppSecret(configuration, ChannelType.Instagram);

        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.instagram.com/oauth/access_token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = appId,
                ["client_secret"] = appSecret,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirectUri,
                ["code"] = code,
            }),
        };
        var tokenResponse = await httpClient.SendAsync(tokenRequest, ct);
        await EnsureSuccessAsync(tokenResponse, "Instagram oauth/access_token", ct);
        using var tokenDoc = JsonDocument.Parse(await tokenResponse.Content.ReadAsStreamAsync(ct));
        var shortLivedToken = tokenDoc.RootElement.GetProperty("access_token").GetString()!;

        var exchangeResponse = await httpClient.GetAsync(
            "https://graph.instagram.com/access_token?grant_type=ig_exchange_token" +
            $"&client_secret={Uri.EscapeDataString(appSecret)}&access_token={Uri.EscapeDataString(shortLivedToken)}", ct);
        await EnsureSuccessAsync(exchangeResponse, "Instagram ig_exchange_token", ct);
        using var exchangeDoc = JsonDocument.Parse(await exchangeResponse.Content.ReadAsStreamAsync(ct));
        var longLivedToken = exchangeDoc.RootElement.GetProperty("access_token").GetString()!;

        var meResponse = await httpClient.GetAsync(
            $"https://graph.instagram.com/{GraphApiVersion}/me?fields=id,username" +
            $"&access_token={Uri.EscapeDataString(longLivedToken)}", ct);
        await EnsureSuccessAsync(meResponse, "Instagram /me", ct);
        using var meDoc = JsonDocument.Parse(await meResponse.Content.ReadAsStreamAsync(ct));
        var accountId = meDoc.RootElement.GetProperty("id").GetString()!;
        var username = meDoc.RootElement.GetProperty("username").GetString()!;

        // Instagram Login (бар хилофи Facebook Pages) як account-и бизнеси якрангаро иҷозат
        // медиҳад — на рӯйхати чандто барои интихоб. Барои шакли якхела бо Facebook (то /connect
        // бе мантиқи алоҳида кор кунад), боз ҳам ҳамчун рӯйхати як-узвӣ бармегардонем.
        var credentialsJson = JsonSerializer.Serialize(new InstagramCredentials(accountId, longLivedToken));
        return [new ConnectableAccount(accountId, username, credentialsJson)];
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string context, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(ct);
        logger.LogError("{Context} хатогӣ: {StatusCode} {Body}", context, (int)response.StatusCode, body);
        throw new MetaOAuthException(context, (int)response.StatusCode, body);
    }
}
