using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Office.Api.Auth;
using Office.Api.Channels.Meta;
using Office.Api.Common;
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
        group.MapGet("/{provider}/callback", CallbackAsync)
            .AllowAnonymous()
            .WithSummary("Callback-и Meta: state-ро месанҷад, code-ро ба token табдил медиҳад, рӯйхати account бармегардонад")
            .Produces<OAuthCallbackResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
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
        CancellationToken ct)
    {
        if (!TryParseOAuthProvider(provider, out var type))
            return ProviderNotSupportedProblem(provider);

        if (!string.IsNullOrEmpty(error))
        {
            return Results.Problem(
                title: "Корбар авторизатсияро рад кард",
                detail: errorDescription ?? error,
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            return InvalidStateProblem();

        var signingKey = GetStateSigningKey(configuration);
        var now = DateTimeOffset.UtcNow;

        if (!OAuthStateCodec.TryDecode(state, signingKey, now, out var statePayload) || statePayload is null)
            return InvalidStateProblem();

        if (!string.Equals(statePayload.Provider, provider, StringComparison.OrdinalIgnoreCase))
            return InvalidStateProblem();

        var stateExpiresAt = DateTimeOffset.FromUnixTimeSeconds(statePayload.ExpiresAtUnix);
        if (!nonceTracker.TryConsume(statePayload.Nonce, stateExpiresAt, now))
            return InvalidStateProblem();

        var connector = connectorFactory.GetConnector(type);
        var redirectUri = BuildRedirectUri(configuration, provider);

        IReadOnlyList<ConnectableAccount> accounts;
        try
        {
            accounts = await connector.ExchangeCodeAsync(code, redirectUri, ct);
        }
        catch (InvalidOperationException)
        {
            return Results.Problem(
                title: "Хатогии Meta",
                detail: "Табдили code ба token муваффақ нашуд. Дубора аз аввал кӯшиш кунед.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        if (accounts.Count == 0)
        {
            return Results.Problem(
                title: "Account ёфт нашуд",
                detail: "Ба ин корбар ҳеҷ Page/account-и қобили пайваст тобеъ нест.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var connectionId = connectionStore.Create(new OAuthConnectionSession(type, statePayload.UserId, stateExpiresAt, accounts));
        var options = accounts.Select(a => new OAuthAccountOption(a.ExternalId, a.Name)).ToList();

        return Results.Ok(new OAuthCallbackResponse(connectionId, options));
    }

    private static IResult InvalidStateProblem() => Results.Problem(
        title: "State-и нодуруст",
        detail: "State эътибор надорад, кӯҳна шудааст ё аллакай истифода шудааст. Аз аввал сар кунед.",
        statusCode: StatusCodes.Status400BadRequest);

    private static bool TryParseOAuthProvider(string provider, out ChannelType type)
    {
        if (Enum.TryParse(provider, ignoreCase: true, out type) && type is ChannelType.Facebook or ChannelType.Instagram)
            return true;

        type = default;
        return false;
    }

    private static IResult ProviderNotSupportedProblem(string provider) => Results.Problem(
        title: "Провайдери нодуруст",
        detail: $"'{provider}' барои OAuth дастгирӣ намешавад. Танҳо facebook ва instagram имконпазир аст " +
                "(WhatsApp тавассути credentials дастӣ пайваст мешавад).",
        statusCode: StatusCodes.Status400BadRequest);

    private static string BuildRedirectUri(IConfiguration configuration, string provider) =>
        $"{MetaOAuthConfig.GetRedirectBaseUrl(configuration)}/api/channels/oauth/{provider}/callback";

    private static string GetStateSigningKey(IConfiguration configuration) =>
        configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key танзим нашудааст.");
}
