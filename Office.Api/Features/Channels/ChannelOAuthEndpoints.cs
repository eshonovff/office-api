using System.Security.Claims;
using System.Security.Cryptography;
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
