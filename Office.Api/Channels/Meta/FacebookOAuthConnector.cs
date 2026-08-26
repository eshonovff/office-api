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

    // message_echoes қасдан дар ин рӯйхат аст: бе он паёме, ки худи корманд мустақим аз барномаи
    // Facebook мефиристад, дар UI намоён намешавад (ниг. FacebookPayloadParser.is_echo).
    private static readonly string[] RequiredWebhookFields = ["messages", "messaging_postbacks", "message_echoes"];
    private const string RequiredWebhookFieldsParam = "messages,messaging_postbacks,message_echoes";

    /// <summary>
    /// Обуна ба webhook — ҳарду сатҳ (Page ва App), баъд ТАСДИҚ бо GET, на танҳо такя ба POST-и
    /// 200. Сабаби ин: 2026-08-25 маҳз ҳамин ду сатҳ аз ҳам ҷудо буданд — Page ба се майдон обуна
    /// буд (subscribed_apps), вале App Dashboard танҳо message_echoes дошт, ва Meta паёми оддиро
    /// ҳеҷ гоҳ намефиристод. Идемпотентӣ — дар ҳар /connect такрор мезанем, то агар касе Dashboard-ро
    /// якбора реset кунад, пайвастшавии навбатӣ худкор ислоҳ кунад. null = ҳарду сатҳ тасдиқ шуд;
    /// вагарна сабаби мушаххас (барои Channel.WebhookSetupWarning — ниг. ChannelOAuthEndpoints).
    /// </summary>
    public async Task<string?> EnsureWebhookSubscriptionAsync(string pageId, string pageAccessToken, CancellationToken ct)
    {
        try
        {
            await SubscribePageAsync(pageId, pageAccessToken, ct);
            await EnsureAppSubscriptionAsync(ct);
        }
        catch (MetaOAuthException ex)
        {
            logger.LogError(ex, "Facebook: обунаи webhook ба Meta нарасид (Page {PageId})", pageId);
            return $"Обунаи webhook ба Meta нарасид: {ex.Context} (HTTP {ex.StatusCode}).";
        }

        var missingAtPage = RequiredWebhookFields.Except(await GetPageSubscribedFieldsAsync(pageId, pageAccessToken, ct)).ToList();
        var missingAtApp = RequiredWebhookFields.Except(await GetAppLevelPageFieldsAsync(ct)).ToList();

        if (missingAtPage.Count == 0 && missingAtApp.Count == 0)
            return null;

        var parts = new List<string>();
        if (missingAtPage.Count > 0)
            parts.Add($"сатҳи Page: {string.Join(", ", missingAtPage)}");
        if (missingAtApp.Count > 0)
            parts.Add($"сатҳи App Dashboard: {string.Join(", ", missingAtApp)}");

        var reason = $"Ин майдонҳо фаъол нестанд — {string.Join("; ", parts)}. Паёмҳо намерасанд.";
        logger.LogWarning("Facebook: обунаи webhook пас аз POST боз ҳам нопурра аст (Page {PageId}): {Reason}", pageId, reason);
        return reason;
    }

    private async Task SubscribePageAsync(string pageId, string pageAccessToken, CancellationToken ct)
    {
        var url = $"{GraphApiBaseUrl}/{GraphApiVersion}/{pageId}/subscribed_apps" +
                  $"?subscribed_fields={RequiredWebhookFieldsParam}&access_token={Uri.EscapeDataString(pageAccessToken)}";
        var response = await httpClient.PostAsync(url, content: null, ct);
        await EnsureSuccessAsync(HttpMethod.Post, url, response, "Facebook subscribed_apps", ct);
    }

    /// <summary>
    /// Сатҳи App (Webhooks product дар App Dashboard) — новобаста аз он, ки Page худаш ба чӣ обуна
    /// аст (SubscribePageAsync боло), Meta танҳо он майдонеро мефиристад, ки дар ин сатҳ низ фаъол
    /// аст.
    /// </summary>
    private async Task EnsureAppSubscriptionAsync(CancellationToken ct)
    {
        var appId = MetaOAuthConfig.GetAppId(configuration, ChannelType.Facebook);
        var appSecret = MetaOAuthConfig.GetAppSecret(configuration, ChannelType.Facebook);
        var callbackUrl = $"{MetaOAuthConfig.GetRedirectBaseUrl(configuration)}/webhooks/facebook";
        var verifyToken = configuration["Webhooks:VerifyToken"]
            ?? throw new InvalidOperationException("Webhooks:VerifyToken танзим нашудааст.");

        var url = $"{GraphApiBaseUrl}/{GraphApiVersion}/{appId}/subscriptions";
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["object"] = "page",
            ["callback_url"] = callbackUrl,
            ["fields"] = RequiredWebhookFieldsParam,
            ["verify_token"] = verifyToken,
            ["access_token"] = $"{appId}|{appSecret}",
        });
        var response = await httpClient.PostAsync(url, form, ct);
        await EnsureSuccessAsync(HttpMethod.Post, url, response, "Facebook app-level subscriptions", ct);
    }

    private async Task<HashSet<string>> GetPageSubscribedFieldsAsync(string pageId, string pageAccessToken, CancellationToken ct)
    {
        var url = $"{GraphApiBaseUrl}/{GraphApiVersion}/{pageId}/subscribed_apps" +
                  $"?fields=subscribed_fields&access_token={Uri.EscapeDataString(pageAccessToken)}";
        var response = await httpClient.GetAsync(url, ct);
        await EnsureSuccessAsync(HttpMethod.Get, url, response, "Facebook subscribed_apps (тасдиқ)", ct);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        if (!doc.RootElement.TryGetProperty("data", out var dataEl) || dataEl.GetArrayLength() == 0 ||
            !dataEl[0].TryGetProperty("subscribed_fields", out var fieldsEl))
            return [];

        return fieldsEl.EnumerateArray().Select(f => f.GetString()!).ToHashSet();
    }

    private async Task<HashSet<string>> GetAppLevelPageFieldsAsync(CancellationToken ct)
    {
        var appId = MetaOAuthConfig.GetAppId(configuration, ChannelType.Facebook);
        var appSecret = MetaOAuthConfig.GetAppSecret(configuration, ChannelType.Facebook);
        var url = $"{GraphApiBaseUrl}/{GraphApiVersion}/{appId}/subscriptions?access_token={Uri.EscapeDataString($"{appId}|{appSecret}")}";
        var response = await httpClient.GetAsync(url, ct);
        await EnsureSuccessAsync(HttpMethod.Get, url, response, "Facebook app-level subscriptions (тасдиқ)", ct);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        if (!doc.RootElement.TryGetProperty("data", out var dataEl))
            return [];

        foreach (var subscription in dataEl.EnumerateArray())
        {
            var isActivePageObject = subscription.TryGetProperty("object", out var objectEl) && objectEl.GetString() == "page" &&
                                      subscription.TryGetProperty("active", out var activeEl) && activeEl.GetBoolean();
            if (!isActivePageObject || !subscription.TryGetProperty("fields", out var fieldsEl))
                continue;

            return fieldsEl.EnumerateArray()
                .Select(f => f.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null)
                .OfType<string>()
                .ToHashSet();
        }

        return [];
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
