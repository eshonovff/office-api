using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Office.Api.Channels.Meta;
using Office.Api.Data;
using Office.Api.Data.Entities;

namespace Office.Api.Features.DataDeletion;

/// <summary>
/// Meta's Data Deletion Request Callback (required for App Review) and its public status page.
/// Both are anonymous by nature — Meta calls the first, anyone with a link opens the second — so
/// all trust comes from the signature (MetaSignedRequest) and from the unguessable code.
/// See docs/phases/phase-14-customer-automations.md, "Қисми 1", for the threat table.
/// </summary>
public static partial class DataDeletionEndpoints
{
    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex CodeFormat();

    public static IEndpointRouteBuilder MapDataDeletionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/meta/data-deletion")
            .WithTags("DataDeletion")
            .AllowAnonymous()
            .RequireRateLimiting("public-callback");

        group.MapPost("/instagram", InstagramAsync)
            .DisableAntiforgery()
            .WithSummary("Callback-и Meta (Instagram app): signed_request → нест кардани маълумоти он корбар")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/status/{code}", StatusAsync)
            .WithSummary("Саҳифаи ҳолати дархости нест кардан (барои корбар, аз рӯи коди Meta)")
            .Produces<string>(StatusCodes.Status200OK, "text/html")
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> InstagramAsync(
        HttpRequest http,
        AppDbContext db,
        IConfiguration configuration,
        IBackgroundJobClient jobs,
        ILogger<DataDeletionJob> logger,
        CancellationToken ct)
    {
        if (!http.HasFormContentType)
            return InvalidRequest();

        var signedRequest = (await http.ReadFormAsync(ct))["signed_request"].ToString();
        var appSecret = configuration[$"Meta:Instagram:AppSecret"] ?? "";

        var payload = MetaSignedRequest.TryVerify(signedRequest, appSecret, DateTimeOffset.UtcNow);
        if (payload is null)
        {
            // Never log the signed_request itself — only that one was refused.
            logger.LogWarning("Data deletion callback refused: signed_request did not verify");
            return InvalidRequest();
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signedRequest))).ToLowerInvariant();
        var existing = await db.DataDeletionRequests.AsNoTracking().FirstOrDefaultAsync(r => r.SignedRequestHash == hash, ct);
        if (existing is not null)
            return Accepted(configuration, existing.ConfirmationCode); // same delivery again: same answer, no second job

        var request = new DataDeletionRequest
        {
            Id = Guid.CreateVersion7(),
            Provider = "instagram",
            MetaUserId = payload.UserId,
            ConfirmationCode = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
            SignedRequestHash = hash,
            Status = DataDeletionStatus.Pending,
            ReceivedAt = DateTimeOffset.UtcNow,
        };
        db.DataDeletionRequests.Add(request);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two deliveries raced past the lookup above; the unique hash let one through.
            var winner = await db.DataDeletionRequests.AsNoTracking().FirstAsync(r => r.SignedRequestHash == hash, ct);
            return Accepted(configuration, winner.ConfirmationCode);
        }

        jobs.Enqueue<DataDeletionJob>(j => j.RunAsync(request.Id, CancellationToken.None));
        return Accepted(configuration, request.ConfirmationCode);
    }

    private static async Task<IResult> StatusAsync(string code, AppDbContext db, CancellationToken ct)
    {
        if (!CodeFormat().IsMatch(code))
            return Results.NotFound();

        var request = await db.DataDeletionRequests.AsNoTracking().FirstOrDefaultAsync(r => r.ConfirmationCode == code, ct);
        if (request is null)
            return Results.NotFound();

        // Status and date only — nothing about whose data or what it was.
        var done = request.Status == DataDeletionStatus.Completed;
        var when = (done ? request.CompletedAt : request.ReceivedAt)?.ToString("yyyy-MM-dd HH:mm 'UTC'") ?? "";
        var html = $$"""
            <!doctype html><html lang="tg"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>office.nizom.tj — data deletion</title>
            <style>body{font-family:system-ui,sans-serif;max-width:32rem;margin:4rem auto;padding:0 1rem;line-height:1.5}</style>
            </head><body>
            <h1>{{(done ? "Маълумот нест карда шуд" : "Дархост қабул шуд")}}</h1>
            <p>{{(done ? "Your data has been deleted." : "Your deletion request is being processed.")}}</p>
            <p>{{when}}</p>
            </body></html>
            """;
        return Results.Content(html, "text/html; charset=utf-8");
    }

    /// <summary>The response shape Meta expects: a status URL and a confirmation code.</summary>
    private static IResult Accepted(IConfiguration configuration, string code) => Results.Json(new
    {
        url = $"{MetaOAuthConfig.GetRedirectBaseUrl(configuration)}/api/meta/data-deletion/status/{code}",
        confirmation_code = code,
    });

    private static IResult InvalidRequest() => Results.Problem(
        title: "signed_request нодуруст",
        statusCode: StatusCodes.Status400BadRequest);
}
