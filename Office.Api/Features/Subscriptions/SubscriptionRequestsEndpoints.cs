using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Email;
using Permissions = Office.Api.Auth.Permissions;

namespace Office.Api.Features.Subscriptions;

/// <summary>
/// Staff side: the moderation queue for customers' payment receipts. The admin page that uses
/// this lives in the staff app and is built separately.
/// </summary>
public static class SubscriptionRequestsEndpoints
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    public static IEndpointRouteBuilder MapSubscriptionRequestsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/subscription-requests").WithTags("SubscriptionRequests");

        group.MapGet("/", ListAsync)
            .RequirePermission(Permissions.Subscriptions.Manage)
            .WithSummary("Дархостҳои обуна — ?status=Pending навбати модератор (аввал кӯҳнатарин)")
            .Produces<IEnumerable<ModeratorSubscriptionRequestDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}/receipt", DownloadReceiptAsync)
            .RequirePermission(Permissions.Subscriptions.Manage)
            .WithSummary("Чеки пардохт (inline — расм ё PDF)")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/approve", ApproveAsync)
            .RequirePermission(Permissions.Subscriptions.Manage)
            .WithSummary("Тасдиқ — тарифи мизоз фаъол/тамдид мешавад")
            .Produces<ModeratorSubscriptionRequestDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/reject", RejectAsync)
            .WithValidation<RejectSubscriptionRequest>()
            .RequirePermission(Permissions.Subscriptions.Manage)
            .WithSummary("Рад кардан — сабаб ҳатмист, ба мизоз нишон дода мешавад")
            .Produces<ModeratorSubscriptionRequestDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> ListAsync(string? status, AppDbContext db, CancellationToken ct)
    {
        var query = db.SubscriptionRequests.AsNoTracking().Include(r => r.Customer).AsQueryable();

        if (!string.IsNullOrEmpty(status))
        {
            if (!Enum.TryParse<SubscriptionRequestStatus>(status, ignoreCase: true, out var parsed))
            {
                return Results.Problem(
                    title: "Статуси нодуруст",
                    detail: "Параметри `status` нодуруст аст.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            query = query.Where(r => r.Status == parsed);
            // The queue is worked oldest-first; everything else reads newest-first.
            query = parsed == SubscriptionRequestStatus.Pending
                ? query.OrderBy(r => r.SubmittedAt)
                : query.OrderByDescending(r => r.CreatedAt);
        }
        else
        {
            query = query.OrderByDescending(r => r.CreatedAt);
        }

        var requests = await query.Take(500).ToListAsync(ct);
        return Results.Ok(requests.Select(ModeratorSubscriptionRequestDto.From));
    }

    private static async Task<IResult> DownloadReceiptAsync(
        Guid id, AppDbContext db, IConfiguration configuration, IWebHostEnvironment env, CancellationToken ct)
    {
        var receiptPath = await db.SubscriptionRequests.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => r.ReceiptPath)
            .FirstOrDefaultAsync(ct);

        var fullPath = SafeUploadsPath.TryResolve(UploadsPathResolver.ResolveRootPath(configuration, env), receiptPath);
        if (fullPath is null || !File.Exists(fullPath))
            return Results.NotFound();

        if (!ContentTypes.TryGetContentType(fullPath, out var contentType))
            contentType = "application/octet-stream";

        // No download file name → served inline, so the moderator sees the image/PDF in place.
        return Results.File(fullPath, contentType);
    }

    private static async Task<IResult> ApproveAsync(
        Guid id, ClaimsPrincipal principal, AppDbContext db, IEmailSender emailSender, CancellationToken ct)
    {
        var reviewer = await LoadReviewerAsync(principal, db, ct);
        var now = DateTimeOffset.UtcNow;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Atomic claim: two moderators approving at once must not extend the plan twice.
        var claimed = await ClaimPendingAsync(db, id, SubscriptionRequestStatus.Approved, reviewer, now, note: null, ct);
        if (claimed is not null)
            return claimed;

        var request = await db.SubscriptionRequests.Include(r => r.Customer).FirstAsync(r => r.Id == id, ct);
        var customer = request.Customer;

        customer.PlanExpiresAt = SubscriptionPeriodCalculator.CalculateNewExpiry(
            now, customer.PlanExpiresAt, customer.TrialEndsAt, request.DurationMonths);
        customer.PlanTier = request.Tier;

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        await emailSender.SendAsync(
            customer.Email,
            "Обунаи шумо фаъол шуд — office.nizom.tj",
            $"Салом, {customer.FullName}!\n\nПардохти шумо тасдиқ шуд. Тарифи {request.Tier} то " +
            $"{customer.PlanExpiresAt.Value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)} фаъол аст.",
            ct);

        return Results.Ok(ModeratorSubscriptionRequestDto.From(request));
    }

    private static async Task<IResult> RejectAsync(
        Guid id,
        RejectSubscriptionRequest body,
        ClaimsPrincipal principal,
        AppDbContext db,
        IEmailSender emailSender,
        CancellationToken ct)
    {
        var reviewer = await LoadReviewerAsync(principal, db, ct);

        var claimed = await ClaimPendingAsync(
            db, id, SubscriptionRequestStatus.Rejected, reviewer, DateTimeOffset.UtcNow, body.Note.Trim(), ct);
        if (claimed is not null)
            return claimed;

        var request = await db.SubscriptionRequests.AsNoTracking().Include(r => r.Customer).FirstAsync(r => r.Id == id, ct);

        await emailSender.SendAsync(
            request.Customer.Email,
            "Пардохт тасдиқ нашуд — office.nizom.tj",
            $"Салом, {request.Customer.FullName}!\n\nЧеки шумо барои тарифи {request.Tier} қабул нашуд.\n" +
            $"Сабаб: {request.ReviewNote}\n\nЛутфан дархости нав фиристед ё ба мо муроҷиат кунед.",
            ct);

        return Results.Ok(ModeratorSubscriptionRequestDto.From(request));
    }

    /// <summary>
    /// Moves Pending → <paramref name="newStatus"/> only if it is still Pending. Returns the error
    /// to send back, or null when this caller won the claim.
    /// </summary>
    private static async Task<IResult?> ClaimPendingAsync(
        AppDbContext db,
        Guid id,
        SubscriptionRequestStatus newStatus,
        User reviewer,
        DateTimeOffset now,
        string? note,
        CancellationToken ct)
    {
        var affected = await db.SubscriptionRequests
            .Where(r => r.Id == id && r.Status == SubscriptionRequestStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, newStatus)
                .SetProperty(r => r.ReviewedByUserId, reviewer.Id)
                .SetProperty(r => r.ReviewedByUserName, reviewer.FullName)
                .SetProperty(r => r.ReviewedAt, now)
                .SetProperty(r => r.ReviewNote, note), ct);

        if (affected == 1)
            return null;

        var exists = await db.SubscriptionRequests.AnyAsync(r => r.Id == id, ct);
        return exists
            ? Results.Problem(
                title: "Дархост дар навбат нест",
                detail: "Ин дархост аллакай баррасӣ шудааст ё ҳанӯз чек надорад.",
                statusCode: StatusCodes.Status409Conflict)
            : Results.NotFound();
    }

    private static Task<User> LoadReviewerAsync(ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        return db.Users.AsNoTracking().FirstAsync(u => u.Id == userId, ct);
    }
}
