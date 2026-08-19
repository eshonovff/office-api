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

        // ҚАДАМИ 1: code → short-lived token. Ин ва ФАҚАТ ин дархост "Instagram oauth/access_token"
        // ном дорад дар лог — параметрҳо дар БАДАН (form-urlencoded), на дар URL.
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
        await EnsureSuccessAsync(tokenRequest, tokenResponse, "Instagram oauth/access_token (step 1: code → short-lived)", ct);
        using var tokenDoc = JsonDocument.Parse(await tokenResponse.Content.ReadAsStreamAsync(ct));
        var shortLivedToken = ExtractShortLivedToken(tokenDoc.RootElement);

        // МУВАҚҚАТӢ — ТАШХИС: дарозӣ=211 тасдиқ кард токен холӣ нест, вале ин арзиши ПУРРАИ
        // токенро дар лог менависад, то бо curl мустақим ба Meta санҷида шавад. Токени
        // кӯтоҳмуддат аст (~1 соат эътибор дорад) — вале ин сатрро БОЯД баъди ташхис нест кард,
        // ин ҷо намемонад (TODO: пас аз санҷиши curl бардоред).
        logger.LogInformation("Instagram step 1 (МУВАҚҚАТӢ, барои санҷиши curl): shortLivedToken={Token}", shortLivedToken);

        // ҚАДАМИ 2: short-lived → long-lived. URL-и ҷудогона, host-и ҷудогона (graph.instagram.com,
        // на api.instagram.com), параметрҳо дар QUERY STRING — ин ва ФАҚАТ ин дархост
        // "Instagram ig_exchange_token" ном дорад. Ду қадам ҳеҷ гоҳ як HttpRequestMessage-ро
        // мубодила намекунанд — ҳар кадом объекти худро дорад, то лог ҳеҷ гоҳ омехта нашавад.
        // GET (на POST) — санҷиши зинда бо URL-и пурраи log-шуда тасдиқ кард: ин endpoint
        // танҳо GET қабул мекунад (POST-и қаблӣ ҳамин ҷо, бо ҳамин URL, 400 дод).
        using var exchangeRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "https://graph.instagram.com/access_token?grant_type=ig_exchange_token" +
            $"&client_secret={Uri.EscapeDataString(appSecret)}&access_token={Uri.EscapeDataString(shortLivedToken)}");
        var exchangeResponse = await httpClient.SendAsync(exchangeRequest, ct);
        await EnsureSuccessAsync(exchangeRequest, exchangeResponse, "Instagram ig_exchange_token (step 2: short-lived → long-lived)", ct);
        using var exchangeDoc = JsonDocument.Parse(await exchangeResponse.Content.ReadAsStreamAsync(ct));
        var longLivedToken = exchangeDoc.RootElement.GetProperty("access_token").GetString()!;

        // ҚАДАМИ 3: маълумоти account бо токени дарозмуддат.
        using var meRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://graph.instagram.com/{GraphApiVersion}/me?fields=id,username&access_token={Uri.EscapeDataString(longLivedToken)}");
        var meResponse = await httpClient.SendAsync(meRequest, ct);
        await EnsureSuccessAsync(meRequest, meResponse, "Instagram /me (step 3: маълумоти account)", ct);
        using var meDoc = JsonDocument.Parse(await meResponse.Content.ReadAsStreamAsync(ct));
        var accountId = meDoc.RootElement.GetProperty("id").GetString()!;
        var username = meDoc.RootElement.GetProperty("username").GetString()!;

        // Instagram Login (бар хилофи Facebook Pages) як account-и бизнеси якрангаро иҷозат
        // медиҳад — на рӯйхати чандто барои интихоб. Барои шакли якхела бо Facebook (то /connect
        // бе мантиқи алоҳида кор кунад), боз ҳам ҳамчун рӯйхати як-узвӣ бармегардонем.
        var credentialsJson = JsonSerializer.Serialize(new InstagramCredentials(accountId, longLivedToken));
        return [new ConnectableAccount(accountId, username, credentialsJson)];
    }

    /// <summary>
    /// Response-и step 1 ду шакл дошта метавонад: ҳуҷҷати ҳозираи Meta (Business Login)
    /// <c>{"data":[{"access_token":...}]}</c> тасвир мекунад, вале баъзе интеграцияҳо (ва
    /// эҳтимол endpoint-и худи Meta низ ҳанӯз) шакли кӯҳнаи flat <c>{"access_token":...}</c>-ро
    /// мегардонанд. Ҳарду кӯшиш мешаванд, то фарзи хато дар шакл боиси токени вайрон нашавад.
    /// </summary>
    private static string ExtractShortLivedToken(JsonElement root)
    {
        if (root.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Array && dataEl.GetArrayLength() > 0 &&
            dataEl[0].TryGetProperty("access_token", out var nestedTokenEl))
        {
            return nestedTokenEl.GetString()!;
        }

        return root.GetProperty("access_token").GetString()!;
    }

    /// <summary>
    /// URL-и ПУРРА (бо client_secret/access_token/code пинҳонкарда — ниг. SensitiveUrlRedactor)
    /// дар лог мемонад, то оянда ягон ислоҳ тахмин набошад: маҳз кадом host, кадом path, кадом
    /// query, кадом усул фиристода шуд — ҳамааш дар як сатр.
    /// </summary>
    private async Task EnsureSuccessAsync(HttpRequestMessage request, HttpResponseMessage response, string context, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(ct);
        var redactedUrl = SensitiveUrlRedactor.Redact(request.RequestUri!.ToString());
        logger.LogError(
            "{Context} хатогӣ: {Method} {Url} -> {StatusCode} {Body}",
            context, request.Method, redactedUrl, (int)response.StatusCode, body);
        throw new MetaOAuthException(context, (int)response.StatusCode, body);
    }
}
