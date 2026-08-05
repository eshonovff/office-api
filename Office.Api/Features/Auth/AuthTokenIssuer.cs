using Office.Api.Auth;
using Office.Api.Data;
using Office.Api.Data.Entities;

namespace Office.Api.Features.Auth;

internal static class AuthTokenIssuer
{
    public const string RefreshCookieName = "refresh_token";
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    public static async Task<string> IssueAsync(
        HttpContext context,
        AppDbContext db,
        ITokenService tokenService,
        IPermissionService permissionService,
        User user,
        CancellationToken ct)
    {
        var permissions = await permissionService.ResolveAsync(user.Id, ct);
        var accessToken = tokenService.CreateAccessToken(user, permissions);
        var refresh = tokenService.CreateRefreshToken();

        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            TokenHash = refresh.Hash,
            ExpiresAt = DateTimeOffset.UtcNow.Add(RefreshTokenLifetime),
            CreatedByIp = context.Connection.RemoteIpAddress?.ToString(),
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(ct);

        var isDevelopment = context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment();
        context.Response.Cookies.Append(RefreshCookieName, refresh.Value, new CookieOptions
        {
            HttpOnly = true,
            Secure = !isDevelopment,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.Add(RefreshTokenLifetime),
            Path = "/",
        });

        return accessToken;
    }
}
