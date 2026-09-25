using Office.Api.Data;
using Office.Api.Data.Entities;

namespace Office.Api.Features.CustomerAuth;

internal static class CustomerAuthTokenIssuer
{
    /// <summary>Номи дигар аз refresh_token-и кормандон — то ду сессия дар як браузер якдигарро напӯшонанд.</summary>
    public const string RefreshCookieName = "customer_refresh_token";
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    public static async Task<string> IssueAsync(
        HttpContext context,
        AppDbContext db,
        ICustomerTokenService tokenService,
        Customer customer,
        CancellationToken ct)
    {
        var accessToken = tokenService.CreateAccessToken(customer);
        var refresh = tokenService.CreateRefreshToken();

        db.CustomerRefreshTokens.Add(new CustomerRefreshToken
        {
            Id = Guid.CreateVersion7(),
            CustomerId = customer.Id,
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
