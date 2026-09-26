using System.Security.Claims;
using System.Text.Json;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Channels.Broadcasts;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Features.Subscriptions;

namespace Office.Api.Features.CustomerBroadcasts;

/// <summary>
/// A мизоҷ's broadcasts (phase 18). Who may see what is the tenant filter's (Broadcast,
/// BroadcastRecipient, Channel, Flow, Conversation — AppDbContext): another мизоҷ's broadcast,
/// channel or flow is simply not found (404). Creating needs a plan; stopping, cancelling and
/// deleting never do. The sending itself is BroadcastSendJob's, under the rules in
/// docs/phases/phase-17-19-customer-growth.md.
/// </summary>
public static class CustomerBroadcastsEndpoints
{
    public const string CreateRateLimitPolicy = "customer-broadcast-create";
    private const int ListLimit = 100;
    private const int FailuresInCard = 50;

    public static IEndpointRouteBuilder MapCustomerBroadcastsEndpoints(this IEndpointRouteBuilder app)
    {
        var broadcasts = app.MapGroup("/api/public/broadcasts")
            .WithTags("CustomerBroadcasts")
            .RequireAuthorization(AuthSchemes.CustomerOnlyPolicy);

        broadcasts.MapGet("/", ListAsync)
            .WithSummary("Рассылкаҳои мизоҷ — аз нав ба кӯҳна")
            .Produces<IReadOnlyList<BroadcastListItem>>(StatusCodes.Status200OK);

        broadcasts.MapGet("/audience", AudienceAsync)
            .WithSummary("Чанд нафар дар аудитория ва чандтояшон ҳоло дастрасанд")
            .Produces<BroadcastAudience>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        broadcasts.MapPost("/", CreateAsync)
            .WithValidation<CreateBroadcastRequest>()
            .RequireRateLimiting(CreateRateLimitPolicy)
            .WithSummary("Рассылкаи нав — ҳозир ё дар вақт; тариф лозим")
            .Produces<BroadcastListItem>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        broadcasts.MapGet("/{id:guid}", GetAsync)
            .WithSummary("Як рассылка бо рақамҳо ва хатоҳо")
            .Produces<BroadcastDetail>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        broadcasts.MapPost("/{id:guid}/cancel", CancelAsync)
            .WithSummary("Бекор (пеш аз оғоз) ё қатъ (ҳангоми фиристодан)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        broadcasts.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Нест кардани рассылкаи тамомшуда аз рӯйхат")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    public static async Task<IResult> ListAsync(Guid? channelId, AppDbContext db, CancellationToken ct)
    {
        var query = db.Broadcasts.AsNoTracking().AsQueryable();
        if (channelId is not null)
            query = query.Where(b => b.ChannelId == channelId);

        var broadcasts = await query.OrderByDescending(b => b.CreatedAt).Take(ListLimit).ToListAsync(ct);
        var counts = await CountsAsync(db, broadcasts.Select(b => b.Id).ToList(), ct);
        return Results.Ok(broadcasts.Select(b => Summary(b, counts.GetValueOrDefault(b.Id) ?? [])).ToList());
    }

    public static async Task<IResult> AudienceAsync(Guid channelId, [FromQuery] string[]? tags, AppDbContext db, CancellationToken ct)
    {
        if (!await db.Channels.AnyAsync(c => c.Id == channelId, ct))
            return Results.NotFound();

        var audience = BroadcastSendJob.Audience(db, channelId, CleanTags(tags));
        return Results.Ok(new BroadcastAudience(
            await audience.CountAsync(ct),
            await BroadcastSendJob.Reachable(db, audience, DateTimeOffset.UtcNow).CountAsync(ct)));
    }

    public static async Task<IResult> CreateAsync(
        CreateBroadcastRequest request, ClaimsPrincipal principal, AppDbContext db, IConfiguration configuration,
        IBackgroundJobClient jobs, CancellationToken ct)
    {
        // Another мизоҷ's channel is "not found" before anything else is said about it.
        var channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == request.ChannelId, ct);
        if (channel is null)
            return Results.NotFound();
        if (channel.Type != ChannelType.Instagram)
            return Results.Problem(detail: "Рассылка танҳо барои Instagram аст.", statusCode: StatusCodes.Status400BadRequest);
        if (!channel.IsActive || channel.RequiresReconnect)
            return Results.Problem(detail: "Аккаунти Instagram-ро аз нав пайваст кунед.", statusCode: StatusCodes.Status409Conflict);

        if (await CustomerEntitlements.LoadLimitsAsync(principal.GetUserId(), db, configuration, ct) is null)
            return CustomerEntitlements.NoAccessProblem();

        if (request.FlowId is { } flowId && !await db.Flows.AnyAsync(f => f.Id == flowId && f.ChannelId == channel.Id && f.IsActive, ct))
            return Results.Problem(
                detail: "Автоматизатсия ёфт нашуд — аз ҳамин аккаунт ва фаъол интихоб кунед.", statusCode: StatusCodes.Status400BadRequest);

        var active = await db.Broadcasts.CountAsync(
            b => b.ChannelId == channel.Id && (b.Status == BroadcastStatus.Scheduled || b.Status == BroadcastStatus.Sending), ct);
        if (active >= BroadcastLimits.MaxActivePerChannel)
            return Results.Problem(
                detail: $"То {BroadcastLimits.MaxActivePerChannel} рассылкаи интизор ё дар раванд барои як аккаунт.", statusCode: StatusCodes.Status409Conflict);

        var now = DateTimeOffset.UtcNow;
        var sendAt = request.ScheduledAt is { } at && at > now ? at : now;
        var isMessage = request.FlowId is null;
        var broadcast = new Broadcast
        {
            Id = Guid.CreateVersion7(),
            ChannelId = channel.Id,
            Name = request.Name.Trim(),
            TagsJson = JsonSerializer.Serialize(CleanTags(request.Tags)),
            Text = isMessage ? NullIfBlank(request.Text) : null,
            MediaId = isMessage ? NullIfBlank(request.MediaId) : null,
            MediaPreviewDataUri = isMessage && !string.IsNullOrEmpty(request.MediaId) ? request.MediaPreviewDataUri : null,
            ButtonTitle = isMessage ? NullIfBlank(request.ButtonTitle) : null,
            ButtonUrl = isMessage ? NullIfBlank(request.ButtonUrl) : null,
            FlowId = request.FlowId,
            Status = BroadcastStatus.Scheduled,
            ScheduledAt = sendAt,
            CreatedAt = now,
        };
        db.Broadcasts.Add(broadcast);
        await db.SaveChangesAsync(ct); // the job must find the row

        broadcast.JobId = sendAt <= now
            ? jobs.Enqueue<BroadcastSendJob>(j => j.RunAsync(broadcast.Id, CancellationToken.None))
            : jobs.Schedule<BroadcastSendJob>(j => j.RunAsync(broadcast.Id, CancellationToken.None), sendAt);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/public/broadcasts/{broadcast.Id}", Summary(broadcast, []));
    }

    public static async Task<IResult> GetAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var broadcast = await db.Broadcasts.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (broadcast is null)
            return Results.NotFound();

        var counts = await CountsAsync(db, [id], ct);
        var flowName = broadcast.FlowId is { } flowId
            ? await db.Flows.Where(f => f.Id == flowId).Select(f => f.Name).FirstOrDefaultAsync(ct)
            : null;
        var failures = await db.BroadcastRecipients
            .Where(r => r.BroadcastId == id && r.Status == BroadcastRecipientStatus.Failed)
            .OrderBy(r => r.ContactId)
            .Take(FailuresInCard)
            .Select(r => new BroadcastFailure(r.ContactId, r.Contact.ContactName, r.Contact.ContactUsername, r.Error))
            .ToListAsync(ct);

        return Results.Ok(new BroadcastDetail(
            Summary(broadcast, counts.GetValueOrDefault(id) ?? []),
            BroadcastSendJob.ReadTags(broadcast),
            broadcast.Text, broadcast.MediaPreviewDataUri, broadcast.ButtonTitle, broadcast.ButtonUrl,
            broadcast.FlowId, flowName, failures));
    }

    public static async Task<IResult> CancelAsync(Guid id, AppDbContext db, IBackgroundJobClient jobs, CancellationToken ct)
    {
        var broadcast = await db.Broadcasts.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (broadcast is null)
            return Results.NotFound();

        switch (broadcast.Status)
        {
            case BroadcastStatus.Scheduled:
                if (broadcast.JobId is not null)
                    jobs.Delete(broadcast.JobId);
                broadcast.Status = BroadcastStatus.Cancelled;
                broadcast.StopRequested = true; // also stops a job that was already starting
                broadcast.FinishedAt = DateTimeOffset.UtcNow;
                broadcast.JobId = null;
                await db.SaveChangesAsync(ct);
                break;
            case BroadcastStatus.Sending:
                // The job stops before its next message. The extra run only closes it (it sees the
                // stop first, sends nothing) — so a stop works even if the batches had died. Once:
                // pressing again changes nothing.
                if (!broadcast.StopRequested)
                {
                    broadcast.StopRequested = true;
                    await db.SaveChangesAsync(ct);
                    jobs.Enqueue<BroadcastSendJob>(j => j.RunAsync(broadcast.Id, CancellationToken.None));
                }
                break;
            default:
                return Results.Problem(detail: "Ин рассылка аллакай тамом шудааст.", statusCode: StatusCodes.Status409Conflict);
        }

        return Results.NoContent();
    }

    public static async Task<IResult> DeleteAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var broadcast = await db.Broadcasts.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (broadcast is null)
            return Results.NotFound();
        if (broadcast.Status is BroadcastStatus.Scheduled or BroadcastStatus.Sending)
            return Results.Problem(detail: "Аввал рассылкаро бекор ё қатъ кунед.", statusCode: StatusCodes.Status409Conflict);

        db.BroadcastRecipients.RemoveRange(await db.BroadcastRecipients.Where(r => r.BroadcastId == id).ToListAsync(ct));
        db.Broadcasts.Remove(broadcast);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>Recipients per status for these broadcasts — counted in the database.</summary>
    private static async Task<Dictionary<Guid, Dictionary<BroadcastRecipientStatus, int>>> CountsAsync(
        AppDbContext db, List<Guid> ids, CancellationToken ct)
    {
        var rows = await db.BroadcastRecipients
            .Where(r => ids.Contains(r.BroadcastId))
            .GroupBy(r => new { r.BroadcastId, r.Status })
            .Select(g => new { g.Key.BroadcastId, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.BroadcastId).ToDictionary(g => g.Key, g => g.ToDictionary(r => r.Status, r => r.Count));
    }

    private static BroadcastListItem Summary(Broadcast b, Dictionary<BroadcastRecipientStatus, int> counts)
    {
        int Count(BroadcastRecipientStatus status) => counts.GetValueOrDefault(status);
        var recipients = counts.Values.Sum();
        return new BroadcastListItem(
            b.Id, b.ChannelId, b.Name, b.Status.ToString(),
            string.IsNullOrEmpty(b.Text) && string.IsNullOrEmpty(b.MediaId) ? "flow" : "message",
            b.ScheduledAt, b.StartedAt, b.FinishedAt,
            recipients,
            Count(BroadcastRecipientStatus.Sent),
            Count(BroadcastRecipientStatus.Failed),
            Count(BroadcastRecipientStatus.SkippedWindowClosed) + Count(BroadcastRecipientStatus.SkippedRecentlyMessaged) +
            Count(BroadcastRecipientStatus.Cancelled),
            b.StartedAt is null ? 0 : Math.Max(b.AudienceCount - recipients, 0),
            b.Error);
    }

    private static string[] CleanTags(string[]? tags) =>
        (tags ?? []).Select(t => t.Trim()).Where(t => t.Length > 0).Distinct().Take(BroadcastLimits.MaxTags).ToArray();

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
