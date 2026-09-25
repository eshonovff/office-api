using Microsoft.EntityFrameworkCore;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Email;

namespace Office.Api.Features.CustomerAuth;

/// <summary>
/// "Forgot password" for a мизоҷ. Both endpoints are anonymous by nature — the protection is
/// the rate limit, an answer that never tells whether an account exists, and a 256-bit,
/// single-use, 30-minute link. Staff are not covered: an Owner resets a staff password.
/// See docs/phases/phase-14-customer-automations.md for the threat table.
/// </summary>
public static class CustomerPasswordResetEndpoints
{
    public static IEndpointRouteBuilder MapCustomerPasswordResetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/public/auth").WithTags("CustomerAuth").AllowAnonymous();

        group.MapPost("/forgot-password", ForgotPassword)
            .WithValidation<ForgotPasswordRequest>()
            .RequireRateLimiting("customer-auth")
            .WithSummary("Пайванди барқарор кардани рамз ба email — ҷавоб ҳамеша якхела аст")
            .Produces<CustomerAuthMessageResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/reset-password", ResetPasswordAsync)
            .WithValidation<ResetPasswordRequest>()
            .RequireRateLimiting("customer-auth")
            .WithSummary("Рамзи нав бо пайванди email — ҳамаи сессияҳои пешина бекор мешаванд")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        return app;
    }

    private static IResult ForgotPassword(
        ForgotPasswordRequest request, PasswordResetQueue queue, ILogger<PasswordResetQueue> logger)
    {
        // The same work and the same answer for every email: the lookup happens later, in the worker.
        if (!queue.TryEnqueue(CustomerAuthEndpoints.NormalizeEmail(request.Email)))
            logger.LogWarning("Password reset queue is full — a request was dropped");

        return Results.Accepted(value: new CustomerAuthMessageResponse(
            "Агар ин email дар система бошад, ба он пайванди барқарор кардани рамз фиристода шуд."));
    }

    private static async Task<IResult> ResetPasswordAsync(
        ResetPasswordRequest request,
        HttpContext context,
        AppDbContext db,
        IEmailSender emailSender,
        ILogger<PasswordResetQueue> logger,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var hash = PasswordResetRules.Hash(request.Token);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Atomic single use: of two requests with the same link, exactly one gets past here.
        var claimed = await db.CustomerPasswordResets
            .Where(r => r.TokenHash == hash && r.ConsumedAt == null && r.ExpiresAt > now && r.Customer.IsActive)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ConsumedAt, now), ct);
        if (claimed == 0)
            return InvalidLinkProblem(); // unknown, used, replaced, expired or switched off — one answer

        var customer = await db.Customers.FirstAsync(c => c.PasswordResets.Any(r => r.TokenHash == hash), ct);
        customer.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        customer.SessionVersion++; // every earlier access token stops working (Program.cs, OnTokenValidated)
        await db.SaveChangesAsync(ct);

        // Every session ends, and any other outstanding link of this account is void.
        await db.CustomerRefreshTokens
            .Where(rt => rt.CustomerId == customer.Id && rt.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(rt => rt.RevokedAt, now), ct);
        await db.CustomerPasswordResets
            .Where(r => r.CustomerId == customer.Id && r.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ConsumedAt, now), ct);

        await transaction.CommitAsync(ct);

        context.Response.Cookies.Delete(CustomerAuthTokenIssuer.RefreshCookieName);
        logger.LogInformation("Customer {CustomerId} set a new password with a reset link", customer.Id);

        await emailSender.SendAsync(
            customer.Email,
            "Рамзи шумо иваз шуд — office.nizom.tj",
            $"Салом, {customer.FullName}!\n\nРамзи ҳисоби шумо дар office.nizom.tj иваз шуд ва аз ҳамаи " +
            "дастгоҳҳо баромад анҷом ёфт.\n\nАгар ин корро шумо накарда бошед, фавран ба мо хабар диҳед.",
            ct);

        return Results.NoContent();
    }

    private static IResult InvalidLinkProblem() => Results.Problem(
        title: "Пайванд нодуруст",
        detail: "Пайванд нодуруст, аллакай истифодашуда ё мӯҳлаташ гузашта аст. Пайванди нав дархост кунед.",
        statusCode: StatusCodes.Status400BadRequest);
}
