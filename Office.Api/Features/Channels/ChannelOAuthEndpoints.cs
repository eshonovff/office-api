using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Channels;
using Office.Api.Channels.Facebook;
using Office.Api.Channels.Instagram;
using Office.Api.Channels.Meta;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Permissions = Office.Api.Auth.Permissions;

namespace Office.Api.Features.Channels;

/// <summary>
/// Пайвасти канал тавассути OAuth-и Meta (Instagram/Facebook) — ба ҷои гузоштани токен дастӣ.
/// WhatsApp тағйир намеёбад: <c>POST /api/channels</c>-и мавҷуда бо credentials дастӣ мемонад.
/// </summary>
public static class ChannelOAuthEndpoints
{
    private const int StateLifetimeMinutes = 10;

    public static IEndpointRouteBuilder MapChannelOAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/channels/oauth").WithTags("Channels OAuth");

        group.MapGet("/{provider}/start", StartAsync)
            .RequirePermission(Permissions.Channels.Manage)
            .WithSummary("URL-и авторизатсияи Meta бо state-и имзошуда ва якмаротибагӣ")
            .Produces<OAuthStartResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // Meta browser-ро мустақим ба ин ҷо redirect мекунад — бе Authorization header.
        // Ҳимоя аз state-и имзошуда меояд, на аз bearer-и муқаррарӣ.
        //
        // Ҳамеша 200 + text/html бармегардонад (муваффақ ё не — фарқе намекунад): SPA-и
        // popup-кушода ин саҳифаро на бо fetch, балки бо худи browser-и popup мекушояд, ва
        // натиҷа тавассути window.postMessage меояд, на HTTP status — ниг. OAuthPostMessagePage.
        group.MapGet("/{provider}/callback", CallbackAsync)
            .AllowAnonymous()
            .WithSummary(
                "Callback-и Meta: state-ро месанҷад, code-ро ба token табдил медиҳад, натиҷаро (рӯйхати account ё хато) " +
                "тавассути window.postMessage ба SPA-и popup-кушода мефиристад — ниг. OAuthPostMessagePage")
            .Produces<string>(StatusCodes.Status200OK, "text/html");

        group.MapPost("/{provider}/connect", ConnectAsync)
            .WithValidation<ConnectChannelRequest>()
            .RequirePermission(Permissions.Channels.Manage)
            .WithSummary("Сохтани/навсозии канал аз account-и интихобшудаи /callback — credentials аз OAuth, на дастӣ")
            .Produces<ChannelDetail>(StatusCodes.Status201Created)
            .Produces<ChannelDetail>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        return app;
    }

    private static IResult StartAsync(
        string provider, ClaimsPrincipal principal, IConfiguration configuration, IChannelOAuthConnectorFactory connectorFactory)
    {
        if (!TryParseOAuthProvider(provider, out var type))
            return ProviderNotSupportedProblem(provider);

        var connector = connectorFactory.GetConnector(type);
        var redirectUri = BuildRedirectUri(configuration, provider);

        var signingKey = GetStateSigningKey(configuration);
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(StateLifetimeMinutes);
        var state = OAuthStateCodec.Encode(provider, principal.GetUserId(), nonce, expiresAt, signingKey);

        var url = connector.BuildAuthorizationUrl(redirectUri, state);
        return Results.Ok(new OAuthStartResponse(url));
    }

    private static async Task<IResult> CallbackAsync(
        string provider,
        string? code,
        string? state,
        string? error,
        [FromQuery(Name = "error_description")] string? errorDescription,
        IConfiguration configuration,
        IChannelOAuthConnectorFactory connectorFactory,
        IOAuthNonceTracker nonceTracker,
        IOAuthConnectionStore connectionStore,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        if (!TryParseOAuthProvider(provider, out var type))
            return PostMessageProblem(ProviderNotSupportedMessage(provider));

        if (!string.IsNullOrEmpty(error))
            return PostMessageProblem(("Корбар авторизатсияро рад кард", errorDescription ?? error));

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            return PostMessageProblem(InvalidStateMessage);

        var signingKey = GetStateSigningKey(configuration);
        var now = DateTimeOffset.UtcNow;

        if (!OAuthStateCodec.TryDecode(state, signingKey, now, out var statePayload) || statePayload is null)
            return PostMessageProblem(InvalidStateMessage);

        if (!string.Equals(statePayload.Provider, provider, StringComparison.OrdinalIgnoreCase))
            return PostMessageProblem(InvalidStateMessage);

        var stateExpiresAt = DateTimeOffset.FromUnixTimeSeconds(statePayload.ExpiresAtUnix);
        if (!nonceTracker.TryConsume(statePayload.Nonce, stateExpiresAt, now))
            return PostMessageProblem(InvalidStateMessage);

        var connector = connectorFactory.GetConnector(type);
        var redirectUri = BuildRedirectUri(configuration, provider);

        IReadOnlyList<ConnectableAccount> accounts;
        try
        {
            accounts = await connector.ExchangeCodeAsync(code, redirectUri, ct);
        }
        catch (MetaOAuthException ex)
        {
            // ex.ResponseBody аллакай дар EnsureSuccessAsync log шудааст — ин ҷо бо {Provider}
            // такрор log мекунем (алоқаи муфид агар дар байни якчанд log жараён гум шавад) ва,
            // муҳимтараш, матни воқеии Meta-ро (на "муваффақ нашуд"-и умумӣ) ба popup мефиристем.
            logger.LogError(ex, "OAuth callback: табдили code ба token барои {Provider} ноком шуд", provider);
            return PostMessageProblem(("Хатогии Meta", TruncateForClient(ex.ResponseBody)));
        }
        catch (Exception ex)
        {
            // Ҳимояи охирин: ҳар хатои ғайричашмдошт (масалан шакли response-и Meta тағйир ёфта
            // бошад) — ошкоро log ва ба popup мефиристад, на 500-и хомӯш бе тафсил.
            logger.LogError(ex, "OAuth callback: хатои ғайричашмдошт барои {Provider}", provider);
            return PostMessageProblem(("Хатогии ғайричашмдошт", TruncateForClient(ex.Message)));
        }

        if (accounts.Count == 0)
            return PostMessageProblem(("Account ёфт нашуд", "Ба ин корбар ҳеҷ Page/account-и қобили пайваст тобеъ нест."));

        var connectionId = connectionStore.Create(
            new OAuthConnectionSession(type, statePayload.UserId, stateExpiresAt, accounts, statePayload.OwnerKind));
        var options = accounts.Select(a => new OAuthAccountOption(a.ExternalId, a.Name)).ToList();

        return PostMessageResult(new OAuthCallbackResponse(connectionId, options));
    }

    private static async Task<IResult> ConnectAsync(
        string provider,
        ConnectChannelRequest request,
        ClaimsPrincipal principal,
        IOAuthConnectionStore connectionStore,
        FacebookOAuthConnector facebookConnector,
        InstagramOAuthConnector instagramConnector,
        AppDbContext db,
        IChannelCredentialsProtector protector,
        CancellationToken ct)
    {
        if (!TryParseOAuthProvider(provider, out var type))
            return ProviderNotSupportedProblem(provider);

        var session = connectionStore.TryGet(request.ConnectionId, DateTimeOffset.UtcNow);
        if (session is null)
            return ConnectionExpiredProblem();

        var account = OAuthConnectPolicy.ResolveAccount(
            session, type, principal.GetUserId(), request.ExternalId, OAuthOwnerKind.Staff);
        if (account is null)
            return ConnectionExpiredProblem();

        var result = await OAuthChannelConnection.SaveAsync(
            type, account, request.Name, ownerCustomerId: null, db, protector, facebookConnector, instagramConnector, ct);

        return result.Outcome switch
        {
            ChannelConnectOutcome.OwnedByAnother => OAuthChannelConnection.OwnedByAnotherProblem(),
            ChannelConnectOutcome.Created => Results.Created(
                $"/api/channels/{result.Channel!.Id}", ChannelsEndpoints.ToDetail(result.Channel)),
            _ => Results.Ok(ChannelsEndpoints.ToDetail(result.Channel!)),
        };
    }

    // Матни хатогии Meta метавонад дароз бошад — token/secret дар он ҳеҷ гоҳ нест (MetaOAuthException
    // танҳо response body-и хатогиро мебардорад, на дархости бо token/secret), пас нишон додани он
    // ба корбар бехатар аст; кӯтоҳ мекунем танҳо барои андозаи UI.
    private const int MaxClientErrorLength = 500;

    private static string TruncateForClient(string text) =>
        text.Length <= MaxClientErrorLength ? text : text[..MaxClientErrorLength] + "…";

    internal static IResult ConnectionExpiredProblem() => Results.Problem(
        title: "Connection эътибор надорад",
        detail: "connectionId кӯҳна шудааст, ба шумо тааллуқ надорад ё account-и хостаро надорад — аз OAuth аз нав сар кунед.",
        statusCode: StatusCodes.Status400BadRequest);

    // Used only by CallbackAsync, which is always a postMessage page (see PostMessageProblem
    // above it) — /start and /connect return normal JSON via their own *Problem() helpers below.
    private static readonly (string Title, string Detail) InvalidStateMessage =
        ("State-и нодуруст", "State эътибор надорад, кӯҳна шудааст ё аллакай истифода шудааст. Аз аввал сар кунед.");

    private static bool TryParseOAuthProvider(string provider, out ChannelType type)
    {
        if (Enum.TryParse(provider, ignoreCase: true, out type) && type is ChannelType.Facebook or ChannelType.Instagram)
            return true;

        type = default;
        return false;
    }

    // Shared text — StartAsync/ConnectAsync return it as normal JSON (ProviderNotSupportedProblem),
    // CallbackAsync as a postMessage page (PostMessageProblem(ProviderNotSupportedMessage(provider))).
    private static (string Title, string Detail) ProviderNotSupportedMessage(string provider) => (
        "Провайдери нодуруст",
        $"'{provider}' барои OAuth дастгирӣ намешавад. Танҳо facebook ва instagram имконпазир аст " +
        "(WhatsApp тавассути credentials дастӣ пайваст мешавад).");

    private static IResult ProviderNotSupportedProblem(string provider)
    {
        var (title, detail) = ProviderNotSupportedMessage(provider);
        return Results.Problem(title: title, detail: detail, statusCode: StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// CallbackAsync's success response — see OAuthPostMessagePage for why (this is a
    /// popup-opened page, not something the SPA calls via fetch).
    /// </summary>
    private static IResult PostMessageResult(object payload) =>
        Results.Content(OAuthPostMessagePage.Build(payload), "text/html");

    private static IResult PostMessageProblem((string Title, string Detail) message) =>
        PostMessageResult(new { title = message.Title, detail = message.Detail });

    internal static string BuildRedirectUri(IConfiguration configuration, string provider) =>
        $"{MetaOAuthConfig.GetRedirectBaseUrl(configuration)}/api/channels/oauth/{provider}/callback";

    internal static string GetStateSigningKey(IConfiguration configuration) =>
        configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key танзим нашудааст.");
}
