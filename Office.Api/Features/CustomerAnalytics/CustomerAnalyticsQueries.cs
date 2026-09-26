using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Office.Api.Channels.Flows;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;

namespace Office.Api.Features.CustomerAnalytics;

/// <summary>
/// The numbers of the analytics page. Every query starts from the DbContext's own sets, so the
/// tenant filter narrows each one to the calling мизоҷ — no raw SQL, no table read around it.
/// Counting and grouping by day happen in the database (days in Dushanbe time, like the staff
/// dashboard); only small results come back. Nothing is cached on the server: a shared cache is
/// one wrong key away from showing one мизоҷ another's numbers.
/// </summary>
public static class CustomerAnalyticsQueries
{
    private const int LocalOffset = OfficeLocalDate.OfficeUtcOffsetHours;
    private const int TopPostCount = 5;
    private const int RecentStartersCount = 50;

    // ── The moments each number is made of (one timestamp per event) ────────────────────────

    private static IQueryable<DateTimeOffset> NewContacts(AppDbContext db, Guid? channelId) =>
        db.Conversations.Where(c => channelId == null || c.ChannelId == channelId).Select(c => c.CreatedAt);

    private static IQueryable<DateTimeOffset> InboundMessages(AppDbContext db, Guid? channelId) =>
        db.Messages
            .Where(m => m.Direction == MessageDirection.Inbound && !m.IsInternalNote)
            .Where(m => channelId == null || m.Conversation.ChannelId == channelId)
            .Select(m => m.CreatedAt);

    /// <summary>What a person sent from Office Nizom (staff or мизоҷ) — not undone, not failed.</summary>
    private static IQueryable<DateTimeOffset> ManualReplies(AppDbContext db, Guid? channelId) =>
        db.Messages
            .Where(m => m.Direction == MessageDirection.Outbound && !m.IsInternalNote && m.SentByUserName != null)
            .Where(m => m.DeliveryStatus != MessageDeliveryStatus.Cancelled && m.DeliveryStatus != MessageDeliveryStatus.Failed)
            .Where(m => channelId == null || m.Conversation.ChannelId == channelId)
            .Select(m => m.CreatedAt);

    /// <summary>A message an automation sent — its step, but not a button click and not one that waited for a closed window.</summary>
    private static IQueryable<DateTimeOffset> FlowMessages(AppDbContext db, Guid? channelId) =>
        db.FlowSessionSteps
            .Where(s => s.FromPort == null || (!s.FromPort.StartsWith(FlowEngine.ButtonPortPrefix) && s.FromPort != FlowEngine.NotSentPort))
            .Where(s => db.FlowNodes.Any(n => n.Id == s.NodeId && n.Type == FlowNodeType.Message))
            .Where(s => channelId == null || s.Session.Flow.ChannelId == channelId)
            .Select(s => s.CreatedAt);

    private static IQueryable<DateTimeOffset> CommentPublicReplies(AppDbContext db, Guid? channelId) =>
        db.AutomationRuns
            .Where(r => r.CommentReplyStatus == AutomationRunStatus.Sent && (channelId == null || r.Rule.ChannelId == channelId))
            .Select(r => r.CreatedAt);

    private static IQueryable<DateTimeOffset> CommentDirects(AppDbContext db, Guid? channelId) =>
        db.AutomationRuns
            .Where(r => r.DmStatus == AutomationRunStatus.Sent && (channelId == null || r.Rule.ChannelId == channelId))
            .Select(r => r.CreatedAt);

    private static IQueryable<DateTimeOffset> BroadcastSends(AppDbContext db, Guid? channelId) =>
        db.BroadcastRecipients
            .Where(r => r.Status == BroadcastRecipientStatus.Sent && r.SentAt != null)
            .Where(r => channelId == null || r.Broadcast.ChannelId == channelId)
            .Select(r => r.SentAt!.Value);

    private static IQueryable<DateTimeOffset> Comments(AppDbContext db, Guid? channelId) =>
        db.InstagramComments.Where(c => !c.IsOwn && (channelId == null || c.ChannelId == channelId)).Select(c => c.CommentedAt);

    private static IQueryable<DateTimeOffset> Within(IQueryable<DateTimeOffset> moments, AnalyticsPeriod period)
    {
        var (start, end) = (period.StartUtc, period.EndUtc);
        return moments.Where(t => t >= start && t < end);
    }

    private static async Task<AnalyticsCount> CountAsync(IQueryable<DateTimeOffset> moments, AnalyticsPeriod period, CancellationToken ct) =>
        new(await Within(moments, period).CountAsync(ct), await Within(moments, period.Previous).CountAsync(ct));

    private static async Task<Dictionary<DateOnly, int>> PerDayAsync(IQueryable<DateTimeOffset> moments, AnalyticsPeriod period, CancellationToken ct)
    {
        var rows = await Within(moments, period)
            .GroupBy(t => t.AddHours(LocalOffset).Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        return rows.ToDictionary(r => DateOnly.FromDateTime(r.Date), r => r.Count);
    }

    // ── The page ────────────────────────────────────────────────────────────────────────────

    public static async Task<AnalyticsOverview> OverviewAsync(AppDbContext db, AnalyticsPeriod period, Guid? channelId, CancellationToken ct)
    {
        IQueryable<DateTimeOffset>[] automatic =
        [
            FlowMessages(db, channelId), CommentPublicReplies(db, channelId), CommentDirects(db, channelId), BroadcastSends(db, channelId),
        ];

        var automaticCount = new AnalyticsCount(0, 0);
        var automaticPerDay = new Dictionary<DateOnly, int>();
        foreach (var source in automatic)
        {
            var count = await CountAsync(source, period, ct);
            automaticCount = new AnalyticsCount(automaticCount.Current + count.Current, automaticCount.Previous + count.Previous);
            foreach (var (date, n) in await PerDayAsync(source, period, ct))
                automaticPerDay[date] = automaticPerDay.GetValueOrDefault(date) + n;
        }

        var contactsPerDay = await PerDayAsync(NewContacts(db, channelId), period, ct);
        var inboundPerDay = await PerDayAsync(InboundMessages(db, channelId), period, ct);
        var (started, converted) = await CohortAsync(db, channelId, flowId: null, period, ct);
        var (startedBefore, convertedBefore) = await CohortAsync(db, channelId, flowId: null, period.Previous, ct);

        return new AnalyticsOverview(
            period.From, period.To,
            await CountAsync(NewContacts(db, channelId), period, ct),
            await CountAsync(InboundMessages(db, channelId), period, ct),
            automaticCount,
            await CountAsync(ManualReplies(db, channelId), period, ct),
            await CountAsync(Comments(db, channelId), period, ct),
            new AnalyticsCount(started, startedBefore),
            new AnalyticsCount(converted, convertedBefore),
            // Empty days are zeros, not gaps — a chart with gaps lies about the shape.
            period.Dates().Select(d => new AnalyticsDay(
                d, contactsPerDay.GetValueOrDefault(d), inboundPerDay.GetValueOrDefault(d), automaticPerDay.GetValueOrDefault(d))).ToList(),
            await BroadcastsAsync(db, channelId, period, ct),
            await TopPostsAsync(db, channelId, period, ct));
    }

    /// <summary>
    /// People × automations started in the period, and of them those who reached the flow's
    /// goal at any time — by start date, so a rate never goes over 100 %.
    /// </summary>
    private static async Task<(int Started, int Converted)> CohortAsync(
        AppDbContext db, Guid? channelId, Guid? flowId, AnalyticsPeriod period, CancellationToken ct)
    {
        var (start, end) = (period.StartUtc, period.EndUtc);
        var starters = db.FlowSessions
            .Where(s => s.CreatedAt >= start && s.CreatedAt < end)
            .Where(s => channelId == null || s.Flow.ChannelId == channelId)
            .Where(s => flowId == null || s.FlowId == flowId)
            .Select(s => new { s.FlowId, s.ContactId })
            .Distinct();

        return (
            await starters.CountAsync(ct),
            await starters.CountAsync(x => db.FlowConversions.Any(c => c.FlowId == x.FlowId && c.ContactId == x.ContactId), ct));
    }

    private static async Task<AnalyticsBroadcasts> BroadcastsAsync(AppDbContext db, Guid? channelId, AnalyticsPeriod period, CancellationToken ct)
    {
        var (start, end) = (period.StartUtc, period.EndUtc);
        var broadcasts = db.Broadcasts
            .Where(b => b.StartedAt != null && b.StartedAt >= start && b.StartedAt < end)
            .Where(b => channelId == null || b.ChannelId == channelId)
            .Select(b => b.Id);

        var byStatus = await db.BroadcastRecipients
            .Where(r => broadcasts.Contains(r.BroadcastId))
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        int Count(params BroadcastRecipientStatus[] statuses) => byStatus.Where(x => statuses.Contains(x.Status)).Sum(x => x.Count);

        return new AnalyticsBroadcasts(
            await broadcasts.CountAsync(ct),
            Count(BroadcastRecipientStatus.Sent),
            Count(BroadcastRecipientStatus.SkippedWindowClosed, BroadcastRecipientStatus.SkippedRecentlyMessaged, BroadcastRecipientStatus.Cancelled),
            Count(BroadcastRecipientStatus.Failed));
    }

    private static async Task<IReadOnlyList<AnalyticsTopPost>> TopPostsAsync(AppDbContext db, Guid? channelId, AnalyticsPeriod period, CancellationToken ct)
    {
        var (start, end) = (period.StartUtc, period.EndUtc);
        var comments = db.InstagramComments
            .Where(c => !c.IsOwn && c.CommentedAt >= start && c.CommentedAt < end)
            .Where(c => channelId == null || c.ChannelId == channelId);

        var top = await comments
            .GroupBy(c => new { c.ChannelId, c.MediaExternalId })
            .Select(g => new { g.Key.ChannelId, g.Key.MediaExternalId, Count = g.Count() })
            .OrderByDescending(x => x.Count).ThenBy(x => x.MediaExternalId)
            .Take(TopPostCount)
            .ToListAsync(ct);

        var result = new List<AnalyticsTopPost>();
        foreach (var post in top)
        {
            // Answered in public by the comment auto-reply: a reply under it, posted by automation.
            var autoReplied = await comments
                .Where(c => c.ChannelId == post.ChannelId && c.MediaExternalId == post.MediaExternalId)
                .CountAsync(c => db.InstagramComments.Any(r =>
                    r.ChannelId == c.ChannelId && r.ParentExternalId == c.ExternalId && r.PostedByAutomation), ct);
            result.Add(new AnalyticsTopPost(post.ChannelId, post.MediaExternalId, post.Count, autoReplied));
        }
        return result;
    }

    // ── Automations ─────────────────────────────────────────────────────────────────────────

    public static async Task<AnalyticsAutomations> AutomationsAsync(AppDbContext db, AnalyticsPeriod period, Guid? channelId, CancellationToken ct)
    {
        var flows = await db.Flows
            .Where(f => channelId == null || f.ChannelId == channelId)
            .Select(f => new { f.Id, f.ChannelId, f.Name, f.IsActive })
            .ToListAsync(ct);
        var flowIds = flows.Select(f => f.Id).ToList();
        var withGoal = await FlowsWithConversionStepAsync(db, flowIds, ct);

        var now = await StartedAndConvertedByFlowAsync(db, channelId, period, ct);
        var before = await StartedAndConvertedByFlowAsync(db, channelId, period.Previous, ct);

        var flowRows = flows
            .Select(f =>
            {
                var (started, converted) = now.GetValueOrDefault(f.Id);
                var (startedBefore, convertedBefore) = before.GetValueOrDefault(f.Id);
                return new AnalyticsFlowRow(
                    f.Id, f.ChannelId, f.Name, f.IsActive, withGoal.Contains(f.Id),
                    new AnalyticsCount(started, startedBefore), new AnalyticsCount(converted, convertedBefore));
            })
            // The switched-off ones only if something happened — the list is what is working.
            .Where(r => r.IsActive || r.Started.Current > 0 || r.Started.Previous > 0)
            .OrderByDescending(r => r.Started.Current).ThenBy(r => r.Name)
            .ToList();

        return new AnalyticsAutomations(period.From, period.To, flowRows, await RuleRowsAsync(db, channelId, period, ct));
    }

    private static async Task<Dictionary<Guid, (int Started, int Converted)>> StartedAndConvertedByFlowAsync(
        AppDbContext db, Guid? channelId, AnalyticsPeriod period, CancellationToken ct)
    {
        var (start, end) = (period.StartUtc, period.EndUtc);
        var sessions = db.FlowSessions
            .Where(s => s.CreatedAt >= start && s.CreatedAt < end)
            .Where(s => channelId == null || s.Flow.ChannelId == channelId);

        var started = await sessions
            .GroupBy(s => s.FlowId)
            .Select(g => new { FlowId = g.Key, Count = g.Select(s => s.ContactId).Distinct().Count() })
            .ToListAsync(ct);
        var converted = await sessions
            .Where(s => db.FlowConversions.Any(c => c.FlowId == s.FlowId && c.ContactId == s.ContactId))
            .GroupBy(s => s.FlowId)
            .Select(g => new { FlowId = g.Key, Count = g.Select(s => s.ContactId).Distinct().Count() })
            .ToListAsync(ct);

        var convertedByFlow = converted.ToDictionary(x => x.FlowId, x => x.Count);
        return started.ToDictionary(x => x.FlowId, x => (x.Count, convertedByFlow.GetValueOrDefault(x.FlowId)));
    }

    /// <summary>Flows with a "Конверсия" step — read from the action nodes' own config, parsed here (never a text search in the database).</summary>
    private static async Task<HashSet<Guid>> FlowsWithConversionStepAsync(AppDbContext db, List<Guid> flowIds, CancellationToken ct)
    {
        var actions = await db.FlowNodes
            .Where(n => flowIds.Contains(n.FlowId) && n.Type == FlowNodeType.Action)
            .Select(n => new { n.FlowId, n.ConfigJson })
            .ToListAsync(ct);

        return actions
            .Where(a => IsConversionStep(a.ConfigJson))
            .Select(a => a.FlowId)
            .ToHashSet();
    }

    private static bool IsConversionStep(string configJson)
    {
        try
        {
            return JsonSerializer.Deserialize<ActionNodeConfig>(configJson, FlowJsonOptions.Options)?.Kind == ActionNodeConfig.KindConversion;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task<IReadOnlyList<AnalyticsRuleRow>> RuleRowsAsync(AppDbContext db, Guid? channelId, AnalyticsPeriod period, CancellationToken ct)
    {
        var (start, end) = (period.StartUtc, period.EndUtc);
        var rules = await db.AutomationRules
            .Where(r => channelId == null || r.ChannelId == channelId)
            .Select(r => new { r.Id, r.ChannelId, r.Name, r.IsActive })
            .ToListAsync(ct);
        var ruleIds = rules.Select(r => r.Id).ToList();

        var runs = db.AutomationRuns.Where(r => ruleIds.Contains(r.RuleId) && r.CreatedAt >= start && r.CreatedAt < end);
        var totals = await runs
            .GroupBy(r => r.RuleId)
            .Select(g => new
            {
                RuleId = g.Key,
                Comments = g.Count(),
                PublicReplies = g.Count(r => r.CommentReplyStatus == AutomationRunStatus.Sent),
                Directs = g.Count(r => r.DmStatus == AutomationRunStatus.Sent),
                Errors = g.Count(r => r.CommentReplyStatus == AutomationRunStatus.Failed || r.DmStatus == AutomationRunStatus.Failed),
            })
            .ToListAsync(ct);
        var totalsByRule = totals.ToDictionary(t => t.RuleId);

        // "Follow, then comment again": who was told so in the period, and of them who later
        // commented again while following — counted per person, not per comment.
        var asked = await runs
            .Where(r => r.FollowCheckResult == FollowCheckResult.NotFollowing)
            .GroupBy(r => new { r.RuleId, r.ActorExternalId })
            .Select(g => new { g.Key.RuleId, g.Key.ActorExternalId, First = g.Min(r => r.CreatedAt) })
            .ToListAsync(ct);
        var followingLater = await db.AutomationRuns
            .Where(r => ruleIds.Contains(r.RuleId) && r.FollowCheckResult == FollowCheckResult.Following && r.CreatedAt >= start)
            .Select(r => new { r.RuleId, r.ActorExternalId, r.CreatedAt })
            .ToListAsync(ct);
        var followed = asked.Where(a => followingLater.Any(f =>
            f.RuleId == a.RuleId && f.ActorExternalId == a.ActorExternalId && f.CreatedAt > a.First)).ToList();

        return rules
            .Select(r =>
            {
                var t = totalsByRule.GetValueOrDefault(r.Id);
                return new AnalyticsRuleRow(
                    r.Id, r.ChannelId, r.Name, r.IsActive,
                    t?.Comments ?? 0, t?.PublicReplies ?? 0, t?.Directs ?? 0, t?.Errors ?? 0,
                    asked.Count(a => a.RuleId == r.Id), followed.Count(a => a.RuleId == r.Id));
            })
            .Where(r => r.IsActive || r.Comments > 0)
            .OrderByDescending(r => r.Comments).ThenBy(r => r.Name)
            .ToList();
    }

    // ── One automation ──────────────────────────────────────────────────────────────────────

    /// <returns>Null when there is no such flow for this caller — another мизоҷ's is "not found".</returns>
    public static async Task<AnalyticsFlowDetail?> FlowDetailAsync(AppDbContext db, Guid flowId, AnalyticsPeriod period, CancellationToken ct)
    {
        var flow = await db.Flows.Where(f => f.Id == flowId).Select(f => new { f.Id, f.Name }).FirstOrDefaultAsync(ct);
        if (flow is null)
            return null;

        var (start, end) = (period.StartUtc, period.EndUtc);
        var sessions = db.FlowSessions.Where(s => s.FlowId == flowId && s.CreatedAt >= start && s.CreatedAt < end);

        var startedPerDay = await sessions
            .GroupBy(s => s.CreatedAt.AddHours(LocalOffset).Date)
            .Select(g => new { Date = g.Key, Count = g.Select(s => s.ContactId).Distinct().Count() })
            .ToListAsync(ct);
        var convertedPerDay = await sessions
            .Where(s => db.FlowConversions.Any(c => c.FlowId == s.FlowId && c.ContactId == s.ContactId))
            .GroupBy(s => s.CreatedAt.AddHours(LocalOffset).Date)
            .Select(g => new { Date = g.Key, Count = g.Select(s => s.ContactId).Distinct().Count() })
            .ToListAsync(ct);
        var startedByDate = startedPerDay.ToDictionary(r => DateOnly.FromDateTime(r.Date), r => r.Count);
        var convertedByDate = convertedPerDay.ToDictionary(r => DateOnly.FromDateTime(r.Date), r => r.Count);

        var recent = await sessions
            .GroupBy(s => s.ContactId)
            .Select(g => new { ContactId = g.Key, StartedAt = g.Max(s => s.CreatedAt) })
            .OrderByDescending(x => x.StartedAt)
            .Take(RecentStartersCount)
            .ToListAsync(ct);
        var ids = recent.Select(r => r.ContactId).ToList();
        var people = await db.Conversations
            .Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.ContactName, c.ContactUsername })
            .ToDictionaryAsync(c => c.Id, ct);
        var convertedIds = (await db.FlowConversions
            .Where(c => c.FlowId == flowId && ids.Contains(c.ContactId))
            .Select(c => c.ContactId)
            .ToListAsync(ct)).ToHashSet();

        var (started, converted) = await CohortAsync(db, channelId: null, flowId, period, ct);
        var (startedBefore, convertedBefore) = await CohortAsync(db, channelId: null, flowId, period.Previous, ct);

        return new AnalyticsFlowDetail(
            flow.Id, flow.Name,
            (await FlowsWithConversionStepAsync(db, [flowId], ct)).Count > 0,
            period.From, period.To,
            new AnalyticsCount(started, startedBefore), new AnalyticsCount(converted, convertedBefore),
            period.Dates().Select(d => new AnalyticsFlowDay(d, startedByDate.GetValueOrDefault(d), convertedByDate.GetValueOrDefault(d))).ToList(),
            recent
                .Where(r => people.ContainsKey(r.ContactId))
                .Select(r => new AnalyticsStarter(
                    r.ContactId, people[r.ContactId].ContactName, people[r.ContactId].ContactUsername, r.StartedAt, convertedIds.Contains(r.ContactId)))
                .ToList());
    }
}
