using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;

namespace Office.Api.Features.Subscriptions;

/// <summary>
/// The customer's side of buying a plan: see prices and cards, get a unique amount to
/// transfer, upload the bank receipt. A moderator then approves it
/// (SubscriptionRequestsEndpoints) — nothing here ever grants a plan by itself.
/// </summary>
public static class CustomerSubscriptionsEndpoints
{
    public static IEndpointRouteBuilder MapCustomerSubscriptionsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/public/subscriptions")
            .WithTags("CustomerSubscriptions")
            .RequireAuthorization(AuthSchemes.CustomerOnlyPolicy);

        group.MapGet("/catalog", GetCatalog)
            .WithSummary("Тарифҳо, нархҳо, муддатҳо ва кортҳое, ки ба онҳо пардохт мешавад")
            .Produces<SubscriptionCatalogResponse>(StatusCodes.Status200OK);

        group.MapGet("/requests", ListMyRequestsAsync)
            .WithSummary("Дархостҳои обунаи худи мизоз — аз нав ба кӯҳна")
            .Produces<IEnumerable<SubscriptionRequestDto>>(StatusCodes.Status200OK);

        group.MapPost("/requests", CreateRequestAsync)
            .WithValidation<CreateSubscriptionRequest>()
            .WithSummary("Дархости нав — маблағи ягонаи гузаронидан (мас. 200.37) ва `paymentDeadline` (Subscriptions:PaymentWindowMinutes) медиҳад")
            .Produces<SubscriptionRequestDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/requests/{id:guid}/receipt", UploadReceiptAsync)
            .DisableAntiforgery()
            .WithSummary("Бор кардани чеки пардохт (jpg/png/webp/pdf, то 10 МБ) + `cardNumber`-и корте, ки ба он гузаронида шуд — дархост ба навбати модератор меравад")
            .Produces<SubscriptionRequestDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static IResult GetCatalog(IConfiguration configuration) =>
        Results.Ok(SubscriptionCatalogResponse.From(SubscriptionCatalog.Load(configuration)));

    private static async Task<IResult> ListMyRequestsAsync(
        ClaimsPrincipal principal, AppDbContext db, IConfiguration configuration, CancellationToken ct)
    {
        var customerId = principal.GetUserId();
        var window = SubscriptionCatalog.Load(configuration).PaymentWindow;
        await SubscriptionRequestExpiry.ExpireOverdueAsync(db, window, DateTimeOffset.UtcNow, ct);

        var requests = await db.SubscriptionRequests.AsNoTracking()
            .Where(r => r.CustomerId == customerId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        return Results.Ok(requests.Select(r => SubscriptionRequestDto.From(r, window)));
    }

    private static async Task<IResult> CreateRequestAsync(
        CreateSubscriptionRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var customerId = principal.GetUserId();
        var catalog = SubscriptionCatalog.Load(configuration);
        var tier = Enum.Parse<CustomerPlanTier>(request.Tier, ignoreCase: true);

        var monthlyPrice = catalog.FindMonthlyPrice(tier);
        if (monthlyPrice is null)
        {
            return Results.Problem(
                title: "Тариф дастрас нест",
                detail: "Ин тарифро ҳоло харидан мумкин нест.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!catalog.DurationMonths.Contains(request.Months))
        {
            return Results.Problem(
                title: "Муддати нодуруст",
                detail: "Ин муддати обуна дастрас нест.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Before reading "open" requests: an expired one neither blocks nor holds its amount.
        var now = DateTimeOffset.UtcNow;
        await SubscriptionRequestExpiry.ExpireOverdueAsync(db, catalog.PaymentWindow, now, ct);

        var open = await db.SubscriptionRequests
            .Where(r => r.CustomerId == customerId &&
                        (r.Status == SubscriptionRequestStatus.AwaitingPayment || r.Status == SubscriptionRequestStatus.Pending))
            .ToListAsync(ct);

        // A receipt is already with a moderator — a second request now invites paying twice.
        if (open.Any(r => r.Status == SubscriptionRequestStatus.Pending))
        {
            return Results.Problem(
                title: "Дархости қаблӣ баррасӣ мешавад",
                detail: "Чеки шумо аллакай дар навбати тасдиқ аст. Лутфан натиҷаро интизор шавед.",
                statusCode: StatusCodes.Status409Conflict);
        }

        foreach (var awaiting in open)
            awaiting.Status = SubscriptionRequestStatus.Cancelled;

        var baseAmount = monthlyPrice.Value * request.Months;
        var takenAmounts = (await db.SubscriptionRequests.AsNoTracking()
                .Where(r => (r.Status == SubscriptionRequestStatus.AwaitingPayment || r.Status == SubscriptionRequestStatus.Pending) &&
                            r.ExpectedAmount > baseAmount && r.ExpectedAmount < baseAmount + 1)
                .Select(r => r.ExpectedAmount)
                .ToListAsync(ct))
            .ToHashSet();

        var subscriptionRequest = new SubscriptionRequest
        {
            Id = Guid.CreateVersion7(),
            CustomerId = customerId,
            Tier = tier,
            DurationMonths = request.Months,
            ExpectedAmount = PaymentAmountGenerator.Generate(baseAmount, takenAmounts, Random.Shared),
            Status = SubscriptionRequestStatus.AwaitingPayment,
            CreatedAt = now,
        };

        db.SubscriptionRequests.Add(subscriptionRequest);
        await db.SaveChangesAsync(ct);

        return Results.Ok(SubscriptionRequestDto.From(subscriptionRequest, catalog.PaymentWindow));
    }

    private static async Task<IResult> UploadReceiptAsync(
        Guid id,
        IFormFile file,
        [FromForm] string? cardNumber,
        ClaimsPrincipal principal,
        AppDbContext db,
        IConfiguration configuration,
        IWebHostEnvironment env,
        CancellationToken ct)
    {
        var customerId = principal.GetUserId();

        // Scoped to the caller's own requests — another customer's id is simply "not found".
        var subscriptionRequest = await db.SubscriptionRequests
            .FirstOrDefaultAsync(r => r.Id == id && r.CustomerId == customerId, ct);
        if (subscriptionRequest is null)
            return Results.NotFound();

        var catalog = SubscriptionCatalog.Load(configuration);
        if (subscriptionRequest.Status == SubscriptionRequestStatus.AwaitingPayment &&
            PaymentWindow.IsTooLateToUpload(subscriptionRequest.CreatedAt, catalog.PaymentWindow, DateTimeOffset.UtcNow))
        {
            subscriptionRequest.Status = SubscriptionRequestStatus.Expired;
            await db.SaveChangesAsync(ct);
        }

        if (subscriptionRequest.Status == SubscriptionRequestStatus.Expired)
        {
            return Results.Problem(
                title: "Вақти пардохт тамом шуд",
                detail: "Ин дархост бекор шуд. Лутфан аз нав тариф интихоб кунед.",
                statusCode: StatusCodes.Status409Conflict);
        }

        // Re-upload is allowed until a moderator has decided (a wrong screenshot is common).
        if (subscriptionRequest.Status is not (SubscriptionRequestStatus.AwaitingPayment or SubscriptionRequestStatus.Pending))
        {
            return Results.Problem(
                title: "Дархост аллакай баррасӣ шудааст",
                detail: "Барои ин дархост чек дигар қабул намешавад.",
                statusCode: StatusCodes.Status409Conflict);
        }

        // Without it the moderator would have to search every bank's history for the amount.
        var card = catalog.FindPaymentCard(cardNumber);
        if (card is null)
        {
            return Results.Problem(
                title: "Корт интихоб нашудааст",
                detail: "Лутфан корте, ки ба он пул гузаронидед, интихоб кунед.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var validationError = SubscriptionReceiptStorage.Validate(file);
        if (validationError is not null)
            return validationError;

        var rootPath = UploadsPathResolver.ResolveRootPath(configuration, env);
        SubscriptionReceiptStorage.DeleteIfExists(rootPath, subscriptionRequest.ReceiptPath);

        subscriptionRequest.ReceiptPath = await SubscriptionReceiptStorage.SaveAsync(rootPath, customerId, file, ct);
        subscriptionRequest.ReceiptFileName = file.FileName;
        subscriptionRequest.PaidToBank = card.Bank;
        subscriptionRequest.PaidToCardNumber = card.CardNumber;
        subscriptionRequest.Status = SubscriptionRequestStatus.Pending;
        subscriptionRequest.SubmittedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return Results.Ok(SubscriptionRequestDto.From(subscriptionRequest, catalog.PaymentWindow));
    }
}
