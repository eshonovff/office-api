using System.Text.Json;
using Office.Api.Channels.Facebook;

namespace Office.Api.Channels.Meta;

/// <summary>Facebook Login for Business — барои пайвасти Page (пеш аз фазаи 7-и воқеӣ, танҳо сохтани канал).</summary>
public class FacebookOAuthConnector(HttpClient httpClient, IConfiguration configuration, ILogger<FacebookOAuthConnector> logger) : IChannelOAuthConnector
{
    private const string GraphApiVersion = "v23.0";
    private const string GraphApiBaseUrl = "https://graph.facebook.com";

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

    public async Task<IReadOnlyList<ConnectableAccount>> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct)
    {
        var appId = MetaOAuthConfig.GetAppId(configuration);
        var appSecret = MetaOAuthConfig.GetAppSecret(configuration);

        var shortLivedToken = await GetTokenFieldAsync(
            $"{GraphApiBaseUrl}/{GraphApiVersion}/oauth/access_token" +
            $"?client_id={Uri.EscapeDataString(appId)}&client_secret={Uri.EscapeDataString(appSecret)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}&code={Uri.EscapeDataString(code)}",
            "Facebook oauth/access_token", ct);

        // Page access token-ҳои /me/accounts аллакай дарозмуддатанд, вақте ки бо
        // user token-и дарозмуддат дархост мешаванд — ниёз ба мубодилаи алоҳида нест.
        var longLivedUserToken = await GetTokenFieldAsync(
            $"{GraphApiBaseUrl}/{GraphApiVersion}/oauth/access_token?grant_type=fb_exchange_token" +
            $"&client_id={Uri.EscapeDataString(appId)}&client_secret={Uri.EscapeDataString(appSecret)}" +
            $"&fb_exchange_token={Uri.EscapeDataString(shortLivedToken)}",
            "Facebook fb_exchange_token", ct);

        var response = await httpClient.GetAsync(
            $"{GraphApiBaseUrl}/{GraphApiVersion}/me/accounts?fields=id,name,access_token" +
            $"&access_token={Uri.EscapeDataString(longLivedUserToken)}", ct);
        await EnsureSuccessAsync(response, "Facebook /me/accounts", ct);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        var accounts = new List<ConnectableAccount>();

        if (doc.RootElement.TryGetProperty("data", out var dataEl))
        {
            foreach (var item in dataEl.EnumerateArray())
            {
                var pageId = item.GetProperty("id").GetString()!;
                var name = item.GetProperty("name").GetString()!;
                var pageAccessToken = item.GetProperty("access_token").GetString()!;
                var credentialsJson = JsonSerializer.Serialize(new FacebookCredentials(pageId, pageAccessToken));
                accounts.Add(new ConnectableAccount(pageId, name, credentialsJson));
            }
        }

        return accounts;
    }

    private async Task<string> GetTokenFieldAsync(string url, string context, CancellationToken ct)
    {
        var response = await httpClient.GetAsync(url, ct);
        await EnsureSuccessAsync(response, context, ct);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string context, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        // Танҳо статус ва матни хатогии Meta ба log мераванд (token дар response-и хатогӣ нест —
        // худи URL-и дархост, ки token дорад, тавассути RemoveAllLoggers() аз log хориҷ шудааст).
        var body = await response.Content.ReadAsStringAsync(ct);
        logger.LogError("{Context} хатогӣ: {StatusCode} {Body}", context, (int)response.StatusCode, body);
        throw new InvalidOperationException($"{context} хатогӣ: {(int)response.StatusCode}");
    }
}
