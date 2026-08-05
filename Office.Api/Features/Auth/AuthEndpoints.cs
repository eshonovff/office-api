using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Common;
using Office.Api.Data;

namespace Office.Api.Features.Auth;

public static class AuthEndpoints
{
    private const string InvalidCredentialsMessage = "Логин ё пароли нодуруст.";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", LoginAsync)
            .WithValidation<LoginRequest>()
            .RequireRateLimiting("login")
            .WithSummary("Воридшавӣ бо логин ва парол — токен ва refresh cookie медиҳад")
            .Produces<LoginResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/refresh", RefreshAsync)
            .WithSummary("Бо refresh cookie токени нав гирифтан (бо ротатсия)")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", LogoutAsync)
            .WithSummary("Баромадан — refresh token-и ҷорӣ revoke мешавад")
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/change-password", ChangePasswordAsync)
            .WithValidation<ChangePasswordRequest>()
            .RequireAuthorization()
            .WithSummary("Иваз кардани парол — баъд ҳамаи токен revoke мешаванд")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/me", MeAsync)
            .RequireAuthorization()
            .WithSummary("Профили корбари ҷорӣ бо роль ва permission-ҳо")
            .Produces<MeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext context,
        AppDbContext db,
        ITokenService tokenService,
        IPermissionService permissionService,
        CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == request.Username, ct);
        if (user is null || !user.IsActive || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            return Results.Problem(
                title: "Хатогии воридшавӣ",
                detail: InvalidCredentialsMessage,
                statusCode: StatusCodes.Status401Unauthorized);
        }

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var accessToken = await AuthTokenIssuer.IssueAsync(context, db, tokenService, permissionService, user, ct);
        var permissions = await permissionService.ResolveAsync(user.Id, ct);

        return Results.Ok(new LoginResponse(accessToken, user.MustChangePassword, MeResponse.From(user, permissions)));
    }

    private static async Task<IResult> RefreshAsync(
        HttpContext context,
        AppDbContext db,
        ITokenService tokenService,
        IPermissionService permissionService,
        CancellationToken ct)
    {
        if (!context.Request.Cookies.TryGetValue(AuthTokenIssuer.RefreshCookieName, out var plainToken) ||
            string.IsNullOrEmpty(plainToken))
        {
            return Results.Unauthorized();
        }

        var hash = tokenService.HashRefreshToken(plainToken);
        var stored = await db.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.TokenHash == hash, ct);

        if (stored is null || stored.RevokedAt is not null || stored.ExpiresAt < DateTimeOffset.UtcNow || !stored.User.IsActive)
            return Results.Unauthorized();

        stored.RevokedAt = DateTimeOffset.UtcNow;

        var accessToken = await AuthTokenIssuer.IssueAsync(context, db, tokenService, permissionService, stored.User, ct);

        return Results.Ok(new { accessToken });
    }

    private static async Task<IResult> LogoutAsync(HttpContext context, AppDbContext db, ITokenService tokenService, CancellationToken ct)
    {
        if (context.Request.Cookies.TryGetValue(AuthTokenIssuer.RefreshCookieName, out var plainToken) &&
            !string.IsNullOrEmpty(plainToken))
        {
            var hash = tokenService.HashRefreshToken(plainToken);
            var stored = await db.RefreshTokens.FirstOrDefaultAsync(rt => rt.TokenHash == hash, ct);
            if (stored is not null && stored.RevokedAt is null)
            {
                stored.RevokedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }

        context.Response.Cookies.Delete(AuthTokenIssuer.RefreshCookieName);
        return Results.NoContent();
    }

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        IPermissionService permissionService,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return Results.Unauthorized();

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
        {
            return Results.Problem(
                title: "Хатогии пароли ҷорӣ",
                detail: "Пароли ҷорӣ нодуруст аст.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.MustChangePassword = false;
        await db.SaveChangesAsync(ct);

        // BumpVersion токенҳои кӯҳнаро revoke мекунад — корбар бояд дубора ворид шавад.
        await permissionService.BumpVersionAsync(user.Id, ct);

        return Results.NoContent();
    }

    private static async Task<IResult> MeAsync(
        ClaimsPrincipal principal,
        AppDbContext db,
        IPermissionService permissionService,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return Results.Unauthorized();

        var permissions = await permissionService.ResolveAsync(userId, ct);
        return Results.Ok(MeResponse.From(user, permissions));
    }
}
