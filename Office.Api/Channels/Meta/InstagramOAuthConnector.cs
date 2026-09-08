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
        var (shortLivedToken, userIdFromStep1) = ExtractTokenAndUserId(tokenDoc.RootElement);

        // Токен воқеӣ будани он аллакай тасдиқ шуд (дарозӣ=211, як бор бо curl санҷида шуд) —
        // арзиши пурра дигар ба лог намеравад. userId ин ҷо log мешавад, то бо id-и /me
        // (қадами 3, поён) муқоиса карда шавад — бо webhook кадомаш мувофиқ меояд.
        logger.LogInformation(
            "Instagram step 1: shortLivedToken дарозӣ={Length}, user_id={UserId}", shortLivedToken.Length, userIdFromStep1);

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
        //
        // 2026-08-25: ин қадам "ҳеҷ гоҳ кор накард" гуфта шуда буд — фарзияи нав, бо далели User-Agent-и
        // media CDN (ниг. Program.cs/BrowserUserAgent): graph.instagram.com низ метавонад ба дархости бе
        // User-Agent бо 302 → HTML ҷавоб диҳад, ки IsSuccessStatusCode-ро намегузарад (200 нест — TRUE
        // мемонад ин ҷо, чунки EnsureSuccessAsync 2xx-ро месанҷад, на Content-Type), вале JsonDocument.Parse
        // поён бо HTML ба NotSupportedException/JsonException меафтад — хатои норавшан, на хатои auth-и возеҳ.
        // AddHttpClient<InstagramOAuthConnector> акнун ҳамон BrowserUserAgent-ро дорад (Program.cs).
        var exchangeResponse = await httpClient.SendAsync(exchangeRequest, ct);
        await EnsureSuccessAsync(exchangeRequest, exchangeResponse, "Instagram ig_exchange_token (step 2: short-lived → long-lived)", ct);
        using var exchangeDoc = JsonDocument.Parse(await exchangeResponse.Content.ReadAsStreamAsync(ct));
        var longLivedToken = exchangeDoc.RootElement.GetProperty("access_token").GetString()!;
        // "expires_in" сонияи то анҷоми эътибор аст (~5184000 ≈ 60 рӯз) — InstagramTokenRefreshJob
        // ба ин такя мекунад, то пеш аз мӯҳлат худкор нав кунад.
        var expiresAt = exchangeDoc.RootElement.TryGetProperty("expires_in", out var expiresInEl) && expiresInEl.TryGetInt64(out var expiresInSeconds)
            ? DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds)
            : (DateTimeOffset?)null;

        // ҚАДАМИ 3: маълумоти account бо токени дарозмуддат.
        using var meRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://graph.instagram.com/{GraphApiVersion}/me?fields=id,username&access_token={Uri.EscapeDataString(longLivedToken)}");
        var meResponse = await httpClient.SendAsync(meRequest, ct);
        await EnsureSuccessAsync(meRequest, meResponse, "Instagram /me (step 3: маълумоти account)", ct);
        using var meDoc = JsonDocument.Parse(await meResponse.Content.ReadAsStreamAsync(ct));
        var meId = meDoc.RootElement.GetProperty("id").GetString()!;
        var username = meDoc.RootElement.GetProperty("username").GetString()!;

        // БОГ (2026-08-19): /me-и қадами 3 id-и app-scoped бармегардонад (масалан 28625914253673240) —
        // на ID-е, ки webhook чун entry[].id мефиристад (17841438754823969, Instagram Business
        // Account ID). Санҷиши зинда инро тасдиқ кард: канали бо id-и /me сохташуда ҳеҷ webhook
        // намеёфт ("Канал ёфт нашуд"). Ҳуҷҷати расмии Meta барои step 1 (Business Login) майдони
        // user_id-ро дар паҳлӯи access_token медиҳад — маҳз барои ҳамин мақсад номгузорӣ шудааст.
        // Онро истифода мебарем; агар набошад (шакли flat-и кӯҳна), ба id-и /me бармегардем —
        // беҳтар аз партофтани канал, вале log возеҳ мегӯяд кадомаш истифода шуд.
        var accountId = userIdFromStep1 ?? meId;
        logger.LogInformation(
            "Instagram step 3: /me id={MeId}, ExternalId-и интихобшуда={ChosenId} (сарчашма={Source})",
            meId, accountId, userIdFromStep1 is not null ? "step1.user_id" : "step3./me.id (захира)");

        // Instagram Login (бар хилофи Facebook Pages) як account-и бизнеси якрангаро иҷозат
        // медиҳад — на рӯйхати чандто барои интихоб. Барои шакли якхела бо Facebook (то /connect
        // бе мантиқи алоҳида кор кунад), боз ҳам ҳамчун рӯйхати як-узвӣ бармегардонем.
        var credentialsJson = JsonSerializer.Serialize(new InstagramCredentials(accountId, longLivedToken));
        return [new ConnectableAccount(accountId, username, credentialsJson, expiresAt)];
    }

    // Барои Instagram "message_echoes" майдони алоҳида НЕСТ (бар хилофи Facebook) — Meta онҳоро
    // худи "messages" дохил мекунад (ниг. developers.facebook.com, тасдиқшуда 2026-08-25).
    private const string RequiredWebhookField = "messages";

    /// <summary>
    /// Обуна ба webhook-и Page-и Instagram, баъд ТАСДИҚ бо GET (на танҳо такя ба POST-и 200) —
    /// ҳамон эҳтиёте, ки барои Facebook лозим шуд (ниг. FacebookOAuthConnector.EnsureWebhookSubscriptionAsync
    /// барои сабаб). Диққат: сатҳи App (App Dashboard-и Instagram)-ро аз ин ҷо тасдиқ карда
    /// НАМЕТАВОНЕМ — graph.facebook.com ва graph.instagram.com ҳарду барои ин намуди app access
    /// token (app-id|app-secret) хато медиҳанд (санҷида шуд 2026-08-25). Дар production ҳоло кор
    /// мекунад (муштарӣ хабар медиҳад — ниг. webhook_logs), вале агар канали НАВ бо ҳамин сабаб (сатҳи
    /// App нопурра) вайрон шавад, ин функсия онро дида наметавонад — танҳо сатҳи Page.
    /// null = сатҳи Page тасдиқ шуд; вагарна сабаби мушаххас.
    /// </summary>
    public async Task<string?> EnsureWebhookSubscriptionAsync(string instagramAccountId, string accessToken, CancellationToken ct)
    {
        var url = $"https://graph.instagram.com/{GraphApiVersion}/{instagramAccountId}/subscribed_apps" +
                  $"?subscribed_fields={RequiredWebhookField}&access_token={Uri.EscapeDataString(accessToken)}";
        try
        {
            using var postRequest = new HttpRequestMessage(HttpMethod.Post, url);
            var postResponse = await httpClient.SendAsync(postRequest, ct);
            await EnsureSuccessAsync(postRequest, postResponse, "Instagram subscribed_apps", ct);

            var getUrl = $"https://graph.instagram.com/{GraphApiVersion}/{instagramAccountId}/subscribed_apps" +
                         $"?fields=subscribed_fields&access_token={Uri.EscapeDataString(accessToken)}";
            using var getRequest = new HttpRequestMessage(HttpMethod.Get, getUrl);
            var getResponse = await httpClient.SendAsync(getRequest, ct);
            await EnsureSuccessAsync(getRequest, getResponse, "Instagram subscribed_apps (тасдиқ)", ct);

            using var doc = JsonDocument.Parse(await getResponse.Content.ReadAsStreamAsync(ct));
            var hasMessages = doc.RootElement.TryGetProperty("data", out var dataEl) && dataEl.GetArrayLength() > 0 &&
                               dataEl[0].TryGetProperty("subscribed_fields", out var fieldsEl) &&
                               fieldsEl.EnumerateArray().Any(f => f.GetString() == RequiredWebhookField);

            if (hasMessages)
                return null;

            logger.LogWarning("Instagram: обунаи webhook пас аз POST боз ҳам нопурра аст (Account {AccountId})", instagramAccountId);
            return "Майдони 'messages' фаъол нест — паёмҳо намерасанд.";
        }
        catch (MetaOAuthException ex)
        {
            logger.LogError(ex, "Instagram: обунаи webhook ба Meta нарасид (Account {AccountId})", instagramAccountId);
            return $"Обунаи webhook ба Meta нарасид: {ex.Context} (HTTP {ex.StatusCode}).";
        }
    }

    /// <summary>
    /// Response-и step 1 ду шакл дошта метавонад: ҳуҷҷати ҳозираи Meta (Business Login)
    /// <c>{"data":[{"access_token":...,"user_id":...}]}</c> тасвир мекунад, вале баъзе
    /// интеграцияҳо (ва эҳтимол endpoint-и худи Meta низ ҳанӯз) шакли кӯҳнаи flat
    /// <c>{"access_token":...,"user_id":...}</c>-ро мегардонанд. Ҳарду кӯшиш мешаванд.
    /// </summary>
    public static (string Token, string? UserId) ExtractTokenAndUserId(JsonElement root)
    {
        if (root.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Array && dataEl.GetArrayLength() > 0 &&
            dataEl[0].TryGetProperty("access_token", out var nestedTokenEl))
        {
            return (nestedTokenEl.GetString()!, ExtractUserId(dataEl[0]));
        }

        var token = root.GetProperty("access_token").GetString()!;
        return (token, ExtractUserId(root));
    }

    // 2026-09-08: production се маротиба афтод бо
    // "The requested operation requires an element of type 'String', but the target
    // element has type 'Number'" — Meta баъзан user_id-ро ҳамчун JSON number
    // бармегардонад (на string, тавре ки ҳуҷҷат нишон медиҳад). Ҳарду шаклро қабул мекунем.
    private static string? ExtractUserId(JsonElement parent)
    {
        if (!parent.TryGetProperty("user_id", out var userIdEl))
            return null;

        return userIdEl.ValueKind switch
        {
            JsonValueKind.String => userIdEl.GetString(),
            JsonValueKind.Number => userIdEl.GetInt64().ToString(),
            _ => null,
        };
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
