using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Office.Api.Channels.Flows;
using Office.Api.Channels.Instagram;
using Office.Api.Data;
using Office.Api.Data.Entities;

namespace Office.Api.Channels.Broadcasts;

/// <summary>
/// Sends one broadcast, a batch at a time: <see cref="BatchSize"/> real sends, then the next
/// batch after <see cref="BatchInterval"/> — slow on purpose, a burst of alike DMs is what gets an
/// account flagged. A Hangfire job has no tenant filter, so EVERY query here is by the
/// broadcast's own ChannelId: the audience, each person's conversation, the flow.
///
/// The rules (docs/phases/phase-17-19-customer-growth.md, phase 18):
/// only open 24-hour windows, checked again right before each message; never a message tag
/// (HUMAN_AGENT is for a person's reply, not for broadcasts); one broadcast per person per 24
/// hours, and one broadcast at a time per account — so two can never race to the same person;
/// stop checked before every message, plan and connection every batch; one row per person, saved
/// after each send, so a rerun never sends to anyone twice. Never retried by Hangfire: one chain
/// of batches per broadcast, and an unexpected error closes it as Failed instead of leaving it
/// "sending" forever.
/// </summary>
[AutomaticRetry(Attempts = 0)]
public class BroadcastSendJob(
    AppDbContext db,
    InstagramProvider instagram,
    FlowEngine flowEngine,
    IBackgroundJobClient jobs,
    ILogger<BroadcastSendJob> logger)
{
    public const int BatchSize = 10;
    public static readonly TimeSpan BatchInterval = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan OnePerPerson = TimeSpan.FromHours(24);

    public async Task RunAsync(Guid broadcastId, CancellationToken ct)
    {
        try
        {
            await RunBatchAsync(broadcastId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Broadcast {BroadcastId}: the batch failed — closing it", broadcastId);
            db.ChangeTracker.Clear(); // whatever failed to save must not be saved again
            var broadcast = await db.Broadcasts.FirstOrDefaultAsync(b => b.Id == broadcastId, CancellationToken.None);
            if (broadcast is not null && broadcast.Status is BroadcastStatus.Scheduled or BroadcastStatus.Sending)
                await CloseAsync(broadcast, BroadcastStatus.Failed, "Хатои система — рассылка қатъ шуд.", CancellationToken.None);
        }
    }

    private async Task RunBatchAsync(Guid broadcastId, CancellationToken ct)
    {
        var broadcast = await db.Broadcasts.Include(b => b.Channel).FirstOrDefaultAsync(b => b.Id == broadcastId, ct);
        if (broadcast is null || broadcast.Status is BroadcastStatus.Finished or BroadcastStatus.Cancelled or BroadcastStatus.Failed)
            return;

        if (broadcast.StopRequested)
        {
            await CloseAsync(broadcast, BroadcastStatus.Cancelled, null, ct);
            return;
        }

        if (await WhyItCannotRunAsync(broadcast, ct) is { } reason)
        {
            await CloseAsync(broadcast, BroadcastStatus.Failed, reason, ct);
            return;
        }

        if (broadcast.Status == BroadcastStatus.Scheduled)
        {
            // One at a time per account: this one waits for the one already sending.
            if (await db.Broadcasts.AnyAsync(b =>
                    b.ChannelId == broadcast.ChannelId && b.Id != broadcast.Id && b.Status == BroadcastStatus.Sending, ct))
            {
                broadcast.JobId = jobs.Schedule<BroadcastSendJob>(j => j.RunAsync(broadcast.Id, CancellationToken.None), BatchInterval);
                await db.SaveChangesAsync(ct);
                return;
            }

            await AddRecipientsAsync(broadcast, ct);
            broadcast.Status = BroadcastStatus.Sending;
            broadcast.StartedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        var sends = 0;
        while (sends < BatchSize)
        {
            // A stop pressed mid-batch takes effect before the next message, not the next batch.
            if (await db.Broadcasts.AnyAsync(b => b.Id == broadcast.Id && b.StopRequested, ct))
            {
                broadcast.StopRequested = true;
                await CloseAsync(broadcast, BroadcastStatus.Cancelled, null, ct);
                return;
            }

            var recipient = await db.BroadcastRecipients
                .Include(r => r.Contact)
                .Where(r => r.BroadcastId == broadcast.Id && r.Status == BroadcastRecipientStatus.Pending)
                .OrderBy(r => r.ContactId)
                .FirstOrDefaultAsync(ct);
            if (recipient is null)
                break;

            if (await SendAsync(broadcast, recipient, ct))
                sends++;
            await SaveRecipientAsync(ct); // after every person — never twice to anyone

            // Instagram refused the account's token (the provider marked it): the rest would be refused too.
            if (broadcast.Channel.RequiresReconnect)
            {
                await CloseAsync(broadcast, BroadcastStatus.Failed, "Аккаунти Instagram бояд аз нав пайваст шавад — рассылка қатъ шуд.", ct);
                return;
            }
        }

        if (await db.BroadcastRecipients.AnyAsync(r => r.BroadcastId == broadcast.Id && r.Status == BroadcastRecipientStatus.Pending, ct))
        {
            broadcast.JobId = jobs.Schedule<BroadcastSendJob>(j => j.RunAsync(broadcast.Id, CancellationToken.None), BatchInterval);
        }
        else
        {
            broadcast.Status = BroadcastStatus.Finished;
            broadcast.FinishedAt = DateTimeOffset.UtcNow;
            broadcast.JobId = null;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>The broadcast's audience: this channel's contacts, with one of the tags if any are chosen.</summary>
    public static IQueryable<Conversation> Audience(AppDbContext db, Guid channelId, IReadOnlyCollection<string> tags)
    {
        // This channel only — the job has no tenant filter to lean on.
        var query = db.Conversations.Where(c => c.ChannelId == channelId);
        if (tags.Count > 0)
            query = query.Where(c => db.ContactTags.Any(t => t.ContactId == c.Id && tags.Contains(t.Tag)));
        return query;
    }

    /// <summary>
    /// Who of the audience a message can reach now: the 24-hour window open (null — never wrote —
    /// is closed), and no broadcast sent to them in the last 24 hours.
    /// </summary>
    public static IQueryable<Conversation> Reachable(AppDbContext db, IQueryable<Conversation> audience, DateTimeOffset now)
    {
        var since = now - OnePerPerson;
        return audience.Where(c =>
            c.WindowExpiresAt != null && c.WindowExpiresAt > now &&
            !db.BroadcastRecipients.Any(r => r.ContactId == c.Id && r.Status == BroadcastRecipientStatus.Sent && r.SentAt > since));
    }

    public static IReadOnlyList<string> ReadTags(Broadcast broadcast) =>
        JsonSerializer.Deserialize<string[]>(broadcast.TagsJson) ?? [];

    private async Task<string?> WhyItCannotRunAsync(Broadcast broadcast, CancellationToken ct)
    {
        var channel = broadcast.Channel;
        if (channel.Type != ChannelType.Instagram)
            return "Рассылка танҳо барои Instagram аст.";
        if (!channel.IsActive || channel.RequiresReconnect)
            return "Аккаунти Instagram ҷудо аст ё бояд аз нав пайваст шавад.";
        if (!await AutomationRunGate.CanRunAsync(channel.Id, db, ct))
            return "Тариф фаъол нест — рассылка қатъ шуд.";
        if (broadcast.FlowId is { } flowId)
        {
            if (!await db.Flows.AnyAsync(f => f.Id == flowId && f.ChannelId == broadcast.ChannelId && f.IsActive, ct))
                return "Автоматизатсия хомӯш ё нест карда шудааст.";
        }
        else if (string.IsNullOrEmpty(broadcast.Text) && string.IsNullOrEmpty(broadcast.MediaId))
        {
            return "Автоматизатсияи ин рассылка нест карда шудааст.";
        }
        return null;
    }

    private async Task AddRecipientsAsync(Broadcast broadcast, CancellationToken ct)
    {
        var audience = Audience(db, broadcast.ChannelId, ReadTags(broadcast));
        broadcast.AudienceCount = await audience.CountAsync(ct);

        var reachable = await Reachable(db, audience, DateTimeOffset.UtcNow).Select(c => c.Id).ToListAsync(ct);
        foreach (var contactId in reachable)
            db.BroadcastRecipients.Add(new BroadcastRecipient { BroadcastId = broadcast.Id, ContactId = contactId });
    }

    /// <returns>True when Instagram was asked to deliver (it counts toward the batch).</returns>
    private async Task<bool> SendAsync(Broadcast broadcast, BroadcastRecipient recipient, CancellationToken ct)
    {
        var contact = recipient.Contact;
        var now = DateTimeOffset.UtcNow;

        // Never another channel's person — the recipients were made from this channel only.
        if (contact.ChannelId != broadcast.ChannelId)
        {
            recipient.Status = BroadcastRecipientStatus.Failed;
            recipient.Error = "Контакт аз аккаунти дигар аст.";
            logger.LogError("Broadcast {BroadcastId}: contact {ContactId} is not on its channel — skipped", broadcast.Id, contact.Id);
            return false;
        }

        if (contact.WindowExpiresAt is not { } windowEnd || windowEnd <= now)
        {
            recipient.Status = BroadcastRecipientStatus.SkippedWindowClosed;
            return false;
        }

        var since = now - OnePerPerson;
        if (await db.BroadcastRecipients.AnyAsync(r =>
                r.ContactId == contact.Id && r.BroadcastId != broadcast.Id &&
                r.Status == BroadcastRecipientStatus.Sent && r.SentAt > since, ct))
        {
            recipient.Status = BroadcastRecipientStatus.SkippedRecentlyMessaged;
            return false;
        }

        try
        {
            if (broadcast.FlowId is { } flowId)
                await StartFlowAsync(broadcast, flowId, recipient, ct);
            else
                await SendMessageAsync(broadcast, contact, ct);

            if (recipient.Status == BroadcastRecipientStatus.Pending)
            {
                recipient.Status = BroadcastRecipientStatus.Sent;
                recipient.SentAt = DateTimeOffset.UtcNow;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Ids only in the log — never the text.
            logger.LogWarning(ex, "Broadcast {BroadcastId}: sending to contact {ContactId} failed", broadcast.Id, contact.Id);
            var reason = ex is GraphApiException ? ex.Message : "Instagram паёмро қабул накард.";
            recipient.Status = BroadcastRecipientStatus.Failed;
            recipient.Error = reason.Length > 500 ? reason[..500] : reason;
        }

        return true;
    }

    private async Task SendMessageAsync(Broadcast broadcast, Conversation contact, CancellationToken ct)
    {
        var channel = broadcast.Channel;
        if (!string.IsNullOrEmpty(broadcast.MediaId))
        {
            await instagram.SendMediaMessageAsync(
                channel, contact.ExternalId, broadcast.MediaId, MessageType.Image, caption: null, isVoiceNote: false, messageTag: null, ct);
        }

        if (string.IsNullOrEmpty(broadcast.Text))
            return;

        var variables = await db.ContactVariables.Where(v => v.ContactId == contact.Id).ToDictionaryAsync(v => v.Key, v => v.Value, ct);
        var text = FlowVariableInterpolator.Interpolate(broadcast.Text, variables, FlowEngine.BuildContactFields(contact));

        if (!string.IsNullOrEmpty(broadcast.ButtonTitle) && !string.IsNullOrEmpty(broadcast.ButtonUrl))
        {
            await instagram.SendButtonMessageAsync(
                channel, contact.ExternalId, text,
                [new InstagramSendButton(broadcast.ButtonTitle, InstagramSendButton.TypeWebUrl, broadcast.ButtonUrl, null)],
                messageTag: null, ct);
        }
        else
        {
            await instagram.SendMessageAsync(channel, contact.ExternalId, text, messageTag: null, ct);
        }
    }

    /// <summary>The flow runs as it would from a trigger; if its first step fails, so did this person.</summary>
    private async Task StartFlowAsync(Broadcast broadcast, Guid flowId, BroadcastRecipient recipient, CancellationToken ct)
    {
        var flow = await db.Flows.FirstAsync(f => f.Id == flowId && f.ChannelId == broadcast.ChannelId, ct);
        var startedAfter = DateTimeOffset.UtcNow;
        await flowEngine.StartAsync(flow, recipient.ContactId, ct);

        var session = await db.FlowSessions
            .Where(s => s.FlowId == flow.Id && s.ContactId == recipient.ContactId && s.CreatedAt >= startedAfter)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (session is null)
        {
            recipient.Status = BroadcastRecipientStatus.Failed; // no first step — nothing was started
            recipient.Error = "Автоматизатсия холӣ аст.";
        }
        else if (session.Status == FlowSessionStatus.Failed)
        {
            recipient.Status = BroadcastRecipientStatus.Failed;
            recipient.Error = session.Error is { Length: > 500 } error ? error[..500] : session.Error ?? "Автоматизатсия ноком шуд.";
        }
    }

    /// <summary>
    /// Saves this person's row. If the person was deleted meanwhile (their data erased — the row
    /// went with them), there is nothing left to record: forget it and go on with the others.
    /// </summary>
    private async Task SaveRecipientAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            foreach (var entry in ex.Entries)
                entry.State = EntityState.Detached;
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task CloseAsync(Broadcast broadcast, BroadcastStatus status, string? error, CancellationToken ct)
    {
        var pending = await db.BroadcastRecipients
            .Where(r => r.BroadcastId == broadcast.Id && r.Status == BroadcastRecipientStatus.Pending)
            .ToListAsync(ct);
        foreach (var recipient in pending)
            recipient.Status = BroadcastRecipientStatus.Cancelled;

        broadcast.Status = status;
        broadcast.Error = error;
        broadcast.FinishedAt = DateTimeOffset.UtcNow;
        broadcast.JobId = null;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Broadcast {BroadcastId} closed: {Status}", broadcast.Id, status);
    }
}
