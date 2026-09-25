using System.Security.Claims;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Email;
using Office.Api.Features.CustomerAuth;
using Office.Api.Features.DataDeletion;

namespace Office.Api.Features.CustomerAccount;

/// <param name="ConfirmEmail">Typed by the мизоҷ — the account is deleted only if it equals their email.</param>
public record DeleteAccountRequest(string ConfirmEmail);

public class DeleteAccountRequestValidator : AbstractValidator<DeleteAccountRequest>
{
    public DeleteAccountRequestValidator()
    {
        RuleFor(x => x.ConfirmEmail).NotEmpty().WithMessage("Email-и худро нависед.");
    }
}

/// <summary>
/// A мизоҷ deletes their own account and everything in it: their channels (with contacts,
/// messages, flows — ChannelDataEraser), payment requests and receipts, sessions, external
/// logins, the customer row. Irreversible, so it takes the typed email as confirmation. The
/// caller is only ever the token's own мизоҷ (bearer token, so no CSRF); every query is by
/// their id, inside their tenant filter.
/// </summary>
public static class CustomerAccountEndpoints
{
    public static IEndpointRouteBuilder MapCustomerAccountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/public/account/delete", DeleteAsync)
            .WithTags("CustomerAccount")
            .RequireAuthorization(AuthSchemes.CustomerOnlyPolicy)
            .RequireRateLimiting("customer-auth")
            .WithValidation<DeleteAccountRequest>()
            .WithSummary("Нест кардани ҳисоби худ ва ҳамаи маълумоти он — email бояд дастӣ тасдиқ шавад")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async Task<IResult> DeleteAsync(
        DeleteAccountRequest request,
        ClaimsPrincipal principal,
        HttpContext http,
        AppDbContext db,
        IEmailSender emailSender,
        IConfiguration configuration,
        IWebHostEnvironment env,
        ILogger<DeleteAccountRequest> logger,
        CancellationToken ct)
    {
        var customerId = principal.GetUserId();
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, ct);
        if (customer is null)
            return Results.NotFound(); // unreachable in practice: the Customer scheme already refuses a deleted account

        if (!string.Equals(request.ConfirmEmail.Trim(), customer.Email, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Problem(
                title: "Email мувофиқ нест",
                detail: "Барои тасдиқ email-и ҳисоби худро айнан нависед.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var email = customer.Email;
        var fullName = customer.FullName;
        var channelIds = await db.Channels.Where(c => c.CustomerId == customerId).Select(c => c.Id).ToListAsync(ct);

        // One SaveChanges for everything (the eraser's): channels go before the customer they
        // reference; payment requests, refresh tokens and external logins follow the customer by
        // the database's ON DELETE CASCADE.
        db.Customers.Remove(customer);
        await ChannelDataEraser.EraseAsync(db, channelIds, ct);
        await db.SaveChangesAsync(ct); // when the мизоҷ had no channels, the eraser saved nothing

        // Files only after the commit.
        var uploadsRoot = UploadsPathResolver.ResolveRootPath(configuration, env);
        ChannelDataEraser.DeleteMediaFolders(uploadsRoot, channelIds);
        var receipts = SafeUploadsPath.TryResolve(uploadsRoot, Path.Combine("subscription-receipts", customerId.ToString()));
        if (receipts is not null && Directory.Exists(receipts))
            Directory.Delete(receipts, recursive: true);

        http.Response.Cookies.Delete(CustomerAuthTokenIssuer.RefreshCookieName);
        logger.LogInformation("Customer {CustomerId} deleted their account ({Channels} channel(s))", customerId, channelIds.Count);

        await emailSender.SendAsync(
            email,
            "Ҳисоби шумо нест карда шуд — office.nizom.tj",
            $"Салом, {fullName}!\n\nҲисоби шумо дар office.nizom.tj ва ҳамаи маълумоти он (аккаунтҳои пайвастшуда, " +
            "автоматизатсияҳо, чатҳо, пардохтҳо) нест карда шуд.\n\nАгар ин корро шумо накарда бошед, фавран ба мо хабар диҳед.",
            ct);

        return Results.NoContent();
    }
}
