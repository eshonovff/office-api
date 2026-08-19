using System.Text.Json;
using Office.Api.Channels.Facebook;
using Office.Api.Data.Entities;

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
        var appId = MetaOAuthConfig.GetAppId(configuration, ChannelType.Facebook);
        return $"https://www.facebook.com/{GraphApiVersion}/dialog/oauth" +
               $"?client_id={Uri.EscapeDataString(appId)}" +
               $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
               $"&state={Uri.EscapeDataString(state)}" +
               $"&scope={Scopes}" +
               "&response_type=code";
    }

    public async Task<IReadOnlyList<ConnectableAccount>> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct)
    {
        var appId = MetaOAuthConfig.GetAppId(configuration, ChannelType.Facebook);
        var appSecret = MetaOAuthConfig.GetAppSecret(configuration, ChannelType.Facebook);

        var shortLivedToken = await GetTokenFieldAsync(
            $"{GraphApiBaseUrl}/{GraphApiVersion}/oauth/access_token" +
            $"?client_id={Uri.EscapeDataString(appId)}&client_secret={Uri.EscapeDataString(appSecret)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}&code={Uri.EscapeDataString(code)}",
            "Facebook oauth/access_token (step 1: code → short-lived)", ct);

        // Page access token-ҳои /me/accounts аллакай дарозмуддатанд, вақте ки бо
        // user token-и дарозмуддат дархост мешаванд — ниёз ба мубодилаи алоҳида нест.
        var longLivedUserToken = await GetTokenFieldAsync(
            $"{GraphApiBaseUrl}/{GraphApiVersion}/oauth/access_token?grant_type=fb_exchange_token" +
            $"&client_id={Uri.EscapeDataString(appId)}&client_secret={Uri.EscapeDataString(appSecret)}" +
            $"&fb_exchange_token={Uri.EscapeDataString(shortLivedToken)}",
            "Facebook fb_exchange_token (step 2: short-lived → long-lived)", ct);

        var accountsUrl = $"{GraphApiBaseUrl}/{GraphApiVersion}/me/accounts?fields=id,name,access_token" +
                           $"&access_token={Uri.EscapeDataString(longLivedUserToken)}";
        var response = await httpClient.GetAsync(accountsUrl, ct);
        await EnsureSuccessAsync(HttpMethod.Get, accountsUrl, response, "Facebook /me/accounts (step 3: рӯйхати Page)", ct);

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

    /// <summary>Обуна кардани Page ба webhook-и messages/messaging_postbacks — қисми /connect, на /callback.</summary>
    public async Task SubscribePageAsync(string pageId, string pageAccessToken, CancellationToken ct)
    {
        var url = $"{GraphApiBaseUrl}/{GraphApiVersion}/{pageId}/subscribed_apps" +
                  $"?subscribed_fields=messages,messaging_postbacks&access_token={Uri.EscapeDataString(pageAccessToken)}";
        var response = await httpClient.PostAsync(url, content: null, ct);
        await EnsureSuccessAsync(HttpMethod.Post, url, response, "Facebook subscribed_apps", ct);
    }

    private async Task<string> GetTokenFieldAsync(string url, string context, CancellationToken ct)
    {
        var response = await httpClient.GetAsync(url, ct);
        await EnsureSuccessAsync(HttpMethod.Get, url, response, context, ct);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }

    /// <summary>
    /// URL-и ПУРРА (бо client_secret/access_token/code пинҳонкарда) дар лог мемонад — то ҳар
    /// хатогии оянда бо host/path/query-и аниқ санҷида шавад, на бо тахмин.
    /// </summary>
    private async Task EnsureSuccessAsync(HttpMethod method, string url, HttpResponseMessage response, string context, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(ct);
        var redactedUrl = SensitiveUrlRedactor.Redact(url);
        logger.LogError("{Context} хатогӣ: {Method} {Url} -> {StatusCode} {Body}", context, method, redactedUrl, (int)response.StatusCode, body);
        throw new MetaOAuthException(context, (int)response.StatusCode, body);
    }
}
