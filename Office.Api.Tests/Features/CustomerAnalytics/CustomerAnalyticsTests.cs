using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Channels.Flows;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Features.CustomerAnalytics;

namespace Office.Api.Tests.Features.CustomerAnalytics;

/// <summary>
/// The analytics endpoints called as мизоҷ A through A's real tenant filter. B has a contact with
/// the SAME Instagram id, comments on the SAME post id, a flow with the SAME name and busier
/// numbers everywhere; the company has its own. Every number A sees must be A's alone — and each
/// number must mean exactly what the page says (days in Dushanbe time, the period before, the
/// cohort rate, a goal counted once).
/// </summary>
public class CustomerAnalyticsTests
{
    private sealed class FixedTenant(TenantIdentity identity) : ITenantContext
    {
        public TenantIdentity Current => identity;
    }

    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly DateOnly _today = OfficeLocalDate.Today(DateTimeOffset.UtcNow);
    private readonly Guid _customerA = Guid.NewGuid();
    private readonly Guid _customerB = Guid.NewGuid();
    private readonly Guid _channelA = Guid.NewGuid();
    private readonly Guid _channelA2 = Guid.NewGuid(); // A's second account
    private readonly Guid _channelB = Guid.NewGuid();
    private readonly Guid _companyChannel = Guid.NewGuid();
    private readonly Guid _a1 = Guid.NewGuid();
    private readonly Guid _a2 = Guid.NewGuid();
    private readonly Guid _a3 = Guid.NewGuid();
    private readonly Guid _b1 = Guid.NewGuid();
    private readonly Guid _c1 = Guid.NewGuid();
    private readonly Guid _flowA = Guid.NewGuid();
    private readonly Guid _flowA2 = Guid.NewGuid();
    private readonly Guid _flowA3 = Guid.NewGuid(); // A's, no goal — a1 starts it too
    private readonly Guid _flowB = Guid.NewGuid();
    private readonly Guid _ruleA = Guid.NewGuid();

    /// <summary>A moment in Dushanbe time: <paramref name="daysAgo"/> local days back, at <paramref name="hour"/>:00 local.</summary>
    private DateTimeOffset At(int daysAgo, int hour = 12) =>
        new DateTimeOffset(_today.AddDays(-daysAgo).ToDateTime(new TimeOnly(hour, 0)), TimeSpan.FromHours(5)).ToUniversalTime();

    public CustomerAnalyticsTests()
    {
        using var db = Open(null);
        db.Customers.AddRange(
            new Customer { Id = _customerA, Email = "a@example.com", FullName = "A" },
            new Customer { Id = _customerB, Email = "b@example.com", FullName = "B" });
        db.Channels.AddRange(
            Channel(_channelA, _customerA), Channel(_channelA2, _customerA), Channel(_channelB, _customerB), Channel(_companyChannel, null));

        db.Conversations.AddRange(
            Contact(_a1, _channelA, "fan-1", At(2)),
            Contact(_a2, _channelA, "fan-2", At(40)), // in the period before
            Contact(_a3, _channelA2, "fan-3", At(1)),
            Contact(_b1, _channelB, "fan-1", At(2)), // the same Instagram id as a1
            Contact(Guid.NewGuid(), _channelB, "fan-9", At(0)),
            Contact(_c1, _companyChannel, "fan-7", At(0)));

        // Messages: three in, one manual reply; a note, an undone reply and an echo don't count as replies.
        db.Messages.AddRange(
            Msg(_a1, MessageDirection.Inbound, At(2, 10)),
            Msg(_a1, MessageDirection.Inbound, At(2, 11)),
            Msg(_a1, MessageDirection.Inbound, At(1, 1)), // 01:00 Dushanbe = 20:00 UTC the day before
            Msg(_a1, MessageDirection.Outbound, At(2, 12), sender: "A"),
            Msg(_a1, MessageDirection.Outbound, At(2, 13)), // an echo — from the phone or an automation
            Msg(_a1, MessageDirection.Outbound, At(2, 14), sender: "A", note: true),
            Msg(_a1, MessageDirection.Outbound, At(2, 15), sender: "A", status: MessageDeliveryStatus.Cancelled));
        for (var i = 0; i < 5; i++)
            db.Messages.Add(Msg(_b1, MessageDirection.Inbound, At(1), sender: null));
        for (var i = 0; i < 7; i++)
            db.Messages.Add(Msg(_c1, MessageDirection.Inbound, At(0)));

        // Flows: A's with a goal; A's second account's without; B's with the same name.
        var messageA = Node(_flowA, FlowNodeType.Message, "{}");
        var goalA = Node(_flowA, FlowNodeType.Action, """{"kind":"conversion"}""");
        var messageB = Node(_flowB, FlowNodeType.Message, "{}");
        db.Flows.AddRange(
            Flow(_flowA, _channelA, "Нарх"), Flow(_flowA2, _channelA2, "Салом"), Flow(_flowA3, _channelA, "Тахфиф"), Flow(_flowB, _channelB, "Нарх"));
        db.FlowNodes.AddRange(messageA, goalA, messageB, Node(_flowA2, FlowNodeType.Action, """{"kind":"add_tags"}"""));

        var s1 = Session(_flowA, _a1, At(2)); // reached the goal
        var s2 = Session(_flowA, _a1, At(1)); // the same person again
        var s3 = Session(_flowA, _a2, At(40)); // the period before
        var sb = Session(_flowB, _b1, At(2));
        // a1 reached flowA's goal — that says nothing about flowA3, which has none.
        var s4 = Session(_flowA3, _a1, At(1));
        db.FlowSessions.AddRange(s1, s2, s3, sb, s4);
        db.FlowSessionSteps.AddRange(
            Step(s1, messageA, "default", At(2)), Step(s1, goalA, "default", At(2)),
            Step(s2, messageA, null, At(1)), // waits for a button — the message went
            Step(s2, messageA, FlowEngine.ButtonPort(0), At(1)), // the click — not a message
            Step(s2, messageA, FlowEngine.NotSentPort, At(1)), // a closed window — nothing went
            Step(s3, messageA, "default", At(40)),
            Step(sb, messageB, "default", At(2)), Step(sb, messageB, "default", At(1)));
        db.FlowConversions.AddRange(
            new FlowConversion { FlowId = _flowA, ContactId = _a1, CreatedAt = At(2) },
            new FlowConversion { FlowId = _flowB, ContactId = _b1, CreatedAt = At(2) });

        // Comment auto-reply: asked to follow, then followed; one Direct failed.
        var ruleB = Guid.NewGuid();
        db.AutomationRules.AddRange(Rule(_ruleA, _channelA), Rule(ruleB, _channelB));
        db.AutomationRuns.AddRange(
            Run(_ruleA, "fan-1", At(2), FollowCheckResult.NotFollowing, AutomationRunStatus.Sent, AutomationRunStatus.Sent),
            Run(_ruleA, "fan-1", At(1), FollowCheckResult.Following, AutomationRunStatus.Sent, AutomationRunStatus.Sent),
            Run(_ruleA, "fan-4", At(1), null, AutomationRunStatus.Sent, AutomationRunStatus.Failed),
            // fan-5 was following BEFORE being asked — never "followed after asking".
            Run(_ruleA, "fan-5", At(3), FollowCheckResult.Following, AutomationRunStatus.Sent, AutomationRunStatus.Disabled),
            Run(_ruleA, "fan-5", At(2), FollowCheckResult.NotFollowing, AutomationRunStatus.Sent, AutomationRunStatus.Disabled),
            Run(ruleB, "fan-1", At(1), FollowCheckResult.NotFollowing, AutomationRunStatus.Sent, AutomationRunStatus.Sent),
            Run(ruleB, "fan-1", At(1), FollowCheckResult.Following, AutomationRunStatus.Sent, AutomationRunStatus.Sent));

        // Comments: A — three on m1 (one answered by the auto-reply), one on m2, and A's own reply.
        db.InstagramComments.AddRange(
            Comment(_channelA, "ca1", "m1", At(2)), Comment(_channelA, "ca2", "m1", At(2)), Comment(_channelA, "ca3", "m1", At(1)),
            Comment(_channelA, "ca4", "m2", At(1)),
            Comment(_channelA, "ca1-reply", "m1", At(2), own: true, parent: "ca1", byAutomation: true));
        for (var i = 0; i < 5; i++)
            db.InstagramComments.Add(Comment(_channelB, $"cb{i}", "m1", At(1))); // the SAME post id

        // Broadcasts: A's reached one, skipped one; B's reached one.
        var broadcastA = Broadcast(_channelA, At(1));
        var broadcastB = Broadcast(_channelB, At(1));
        db.Broadcasts.AddRange(broadcastA, broadcastB);
        db.BroadcastRecipients.AddRange(
            new BroadcastRecipient { BroadcastId = broadcastA.Id, ContactId = _a1, Status = BroadcastRecipientStatus.Sent, SentAt = At(1) },
            new BroadcastRecipient { BroadcastId = broadcastA.Id, ContactId = _a2, Status = BroadcastRecipientStatus.SkippedWindowClosed },
            new BroadcastRecipient { BroadcastId = broadcastB.Id, ContactId = _b1, Status = BroadcastRecipientStatus.Sent, SentAt = At(1) });
        db.SaveChanges();
    }

    // ── Seeding helpers ─────────────────────────────────────────────────────────────────────

    private static Channel Channel(Guid id, Guid? owner) =>
        new() { Id = id, Type = ChannelType.Instagram, Name = "ig", ExternalId = id.ToString(), CustomerId = owner, IsActive = true };

    private static Conversation Contact(Guid id, Guid channelId, string externalId, DateTimeOffset createdAt) =>
        new() { Id = id, ChannelId = channelId, ExternalId = externalId, ContactName = externalId, ContactUsername = externalId, CreatedAt = createdAt };

    private static Message Msg(
        Guid contactId, MessageDirection direction, DateTimeOffset at, string? sender = null, bool note = false,
        MessageDeliveryStatus status = MessageDeliveryStatus.Delivered) => new()
    {
        Id = Guid.NewGuid(), ConversationId = contactId, Direction = direction, Type = MessageType.Text, Body = "x",
        SentByUserName = sender, IsInternalNote = note, DeliveryStatus = status, CreatedAt = at,
    };

    private static Flow Flow(Guid id, Guid channelId, string name) => new()
    {
        Id = id, ChannelId = channelId, Name = name, IsActive = true, TriggerType = "instagram_dm", TriggerConfigJson = "{}",
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static FlowNode Node(Guid flowId, FlowNodeType type, string config) =>
        new() { Id = Guid.NewGuid(), FlowId = flowId, Type = type, ConfigJson = config };

    private static FlowSession Session(Guid flowId, Guid contactId, DateTimeOffset at) =>
        new() { Id = Guid.NewGuid(), FlowId = flowId, ContactId = contactId, Status = FlowSessionStatus.Finished, CreatedAt = at };

    private static FlowSessionStep Step(FlowSession session, FlowNode node, string? port, DateTimeOffset at) =>
        new() { Id = Guid.NewGuid(), SessionId = session.Id, NodeId = node.Id, FromPort = port, CreatedAt = at };

    private static AutomationRule Rule(Guid id, Guid channelId) => new()
    {
        Id = id, ChannelId = channelId, Name = "Нарх дар шарҳ", IsActive = true, TriggerType = "instagram_comment",
        TriggerConfigJson = "{}", ActionConfigJson = "{}", CreatedAt = DateTimeOffset.UtcNow,
    };

    private static AutomationRun Run(
        Guid ruleId, string actor, DateTimeOffset at, FollowCheckResult? follow, AutomationRunStatus reply, AutomationRunStatus dm) => new()
    {
        Id = Guid.NewGuid(), RuleId = ruleId, TriggerExternalId = Guid.NewGuid().ToString(), ActorExternalId = actor,
        FollowCheckResult = follow, CommentReplyStatus = reply, DmStatus = dm, CreatedAt = at,
    };

    private static InstagramComment Comment(
        Guid channelId, string id, string media, DateTimeOffset at, bool own = false, string? parent = null, bool byAutomation = false) => new()
    {
        Id = Guid.NewGuid(), ChannelId = channelId, ExternalId = id, MediaExternalId = media, AuthorExternalId = own ? "me" : "fan",
        Text = "нарх?", CommentedAt = at, ReceivedAt = at, IsOwn = own, ParentExternalId = parent, PostedByAutomation = byAutomation,
    };

    private static Broadcast Broadcast(Guid channelId, DateTimeOffset startedAt) => new()
    {
        Id = Guid.NewGuid(), ChannelId = channelId, Name = "b", Text = "t", Status = BroadcastStatus.Finished,
        ScheduledAt = startedAt, StartedAt = startedAt, FinishedAt = startedAt, CreatedAt = startedAt,
    };

    private AppDbContext Open(TenantIdentity? tenant) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_databaseName).Options,
        tenant is null ? null : new FixedTenant(tenant.Value));

    private AppDbContext AsA() => Open(new TenantIdentity(TenantScope.Customer, _customerA));

    private static int? Status(IResult result) => (result as IStatusCodeHttpResult)?.StatusCode;

    private async Task<AnalyticsOverview> Overview(Guid? channelId = null)
    {
        await using var db = AsA();
        return Assert.IsType<Ok<AnalyticsOverview>>(await CustomerAnalyticsEndpoints.OverviewAsync(null, null, channelId, db, CancellationToken.None)).Value!;
    }

    // ── What A sees ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Overview_CountsOnlyTheCallersRows_WithThePeriodBefore()
    {
        var o = await Overview();

        Assert.Equal((_today.AddDays(-29), _today), (o.From, o.To));
        Assert.Equal(new AnalyticsCount(2, 1), o.NewContacts); // a1, a3 — a2 was 40 days ago
        Assert.Equal(new AnalyticsCount(3, 0), o.InboundMessages); // not B's 5, not the company's 7
        Assert.Equal(new AnalyticsCount(1, 0), o.ManualReplies); // not the echo, the note or the undone one
        // Automatic: 2 flow messages (not the click, not the closed window) + 5 public replies
        // + 2 Directs + 1 broadcast; before: the 40-day-old flow message.
        Assert.Equal(new AnalyticsCount(10, 1), o.AutomaticReplies);
        Assert.Equal(new AnalyticsCount(4, 0), o.Comments); // not A's own reply, not B's 5 on the same post
        Assert.Equal(new AnalyticsCount(2, 1), o.Started); // a1 in flowA once (two sessions) + a1 in flowA3
        Assert.Equal(new AnalyticsCount(1, 0), o.Converted); // only flowA's goal — never another flow's
        Assert.Equal(new AnalyticsBroadcasts(1, 1, 1, 0), o.Broadcasts);
    }

    [Fact]
    public async Task Days_AreDushanbeDays_AndEmptyDaysAreZeros()
    {
        var o = await Overview();

        Assert.Equal(30, o.Days.Count);
        Assert.All(o.Days.SkipLast(4), d => Assert.Equal(new AnalyticsDay(d.Date, 0, 0, 0), d));
        Assert.Equal(new AnalyticsDay(_today.AddDays(-3), 0, 0, 1), o.Days.Single(d => d.Date == _today.AddDays(-3))); // fan-5's reply
        var twoDaysAgo = o.Days.Single(d => d.Date == _today.AddDays(-2));
        var yesterday = o.Days.Single(d => d.Date == _today.AddDays(-1));
        Assert.Equal(2, twoDaysAgo.InboundMessages);
        Assert.Equal(1, yesterday.InboundMessages); // 01:00 Dushanbe is yesterday, not the day before in UTC
        Assert.Equal((1, 1), (twoDaysAgo.NewContacts, yesterday.NewContacts));
        Assert.Equal(10, o.Days.Sum(d => d.AutomaticReplies));
    }

    [Fact]
    public async Task TopPosts_AreTheCallersOwn_EvenOnTheSamePostIdAsAnother()
    {
        var o = await Overview();

        Assert.Equal(
            [new AnalyticsTopPost(_channelA, "m1", 3, 1), new AnalyticsTopPost(_channelA, "m2", 1, 0)],
            o.TopPosts);
    }

    [Fact]
    public async Task OneAccount_OnlyItsNumbers()
    {
        var o = await Overview(_channelA2);

        Assert.Equal(new AnalyticsCount(1, 0), o.NewContacts); // a3
        Assert.Equal(0, o.InboundMessages.Current);
        Assert.Equal(0, o.AutomaticReplies.Current);
        Assert.Empty(o.TopPosts);
    }

    [Fact]
    public async Task AnotherMizojsAccount_OrTheCompanys_IsNotFound()
    {
        await using var db = AsA();
        foreach (var channel in new[] { _channelB, _companyChannel })
        {
            Assert.Equal(404, Status(await CustomerAnalyticsEndpoints.OverviewAsync(null, null, channel, db, CancellationToken.None)));
            Assert.Equal(404, Status(await CustomerAnalyticsEndpoints.AutomationsAsync(null, null, channel, db, CancellationToken.None)));
        }
    }

    [Fact]
    public async Task Automations_EachFlowAndRule_OnlyTheCallers()
    {
        await using var db = AsA();
        var result = Assert.IsType<Ok<AnalyticsAutomations>>(
            await CustomerAnalyticsEndpoints.AutomationsAsync(null, null, null, db, CancellationToken.None)).Value!;

        Assert.Equal([_flowA, _flowA3, _flowA2], result.Flows.Select(f => f.FlowId)); // busiest first; not B's "Нарх"
        var flowA = result.Flows[0];
        Assert.True(flowA.HasConversionStep);
        Assert.Equal((new AnalyticsCount(1, 1), new AnalyticsCount(1, 0)), (flowA.Started, flowA.Converted));
        var flowA3 = result.Flows[1];
        Assert.Equal((false, new AnalyticsCount(1, 0), new AnalyticsCount(0, 0)), (flowA3.HasConversionStep, flowA3.Started, flowA3.Converted));
        Assert.False(result.Flows[2].HasConversionStep);

        var rule = Assert.Single(result.Rules);
        Assert.Equal(
            (_ruleA, 5, 5, 2, 1, 2, 1), // fan-5 was asked but had followed before — not "after asking"
            (rule.RuleId, rule.Comments, rule.PublicReplies, rule.DirectMessages, rule.Errors, rule.AskedToFollow, rule.FollowedAfterAsking));
    }

    [Fact]
    public async Task OneAutomation_DaysAndStarters_AndAnothersIsNotFound()
    {
        await using var db = AsA();
        var detail = Assert.IsType<Ok<AnalyticsFlowDetail>>(
            await CustomerAnalyticsEndpoints.FlowDetailAsync(_flowA, null, null, db, CancellationToken.None)).Value!;

        Assert.Equal((new AnalyticsCount(1, 1), new AnalyticsCount(1, 0)), (detail.Started, detail.Converted));
        Assert.Equal(1, detail.Days.Single(d => d.Date == _today.AddDays(-2)).Started);
        var starter = Assert.Single(detail.RecentStarters);
        Assert.Equal((_a1, "fan-1", true), (starter.ContactId, starter.Username, starter.Converted));

        Assert.Equal(404, Status(await CustomerAnalyticsEndpoints.FlowDetailAsync(_flowB, null, null, db, CancellationToken.None)));
    }

    [Fact]
    public async Task ARateNeverPasses100_TheGoalCountsOncePerPerson()
    {
        // a1 passed the goal step again in a new session — still one person, one goal.
        await using (var all = Open(null))
        {
            all.FlowSessions.Add(Session(_flowA, _a1, At(0)));
            await all.SaveChangesAsync();
        }

        var o = await Overview();
        Assert.True(o.Converted.Current <= o.Started.Current);
        Assert.Equal(new AnalyticsCount(2, 1), o.Started);
        Assert.Equal(1, o.Converted.Current);
    }

    // ── The period ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(5, 10)] // from after to
    [InlineData(400, 0)] // over 366 days
    [InlineData(0, -1)] // ends tomorrow
    public async Task ABadPeriod_IsRefused(int fromDaysAgo, int toDaysAgo)
    {
        await using var db = AsA();
        var result = await CustomerAnalyticsEndpoints.OverviewAsync(
            _today.AddDays(-fromDaysAgo), _today.AddDays(-toDaysAgo), null, db, CancellationToken.None);

        Assert.Equal(400, Status(result));
    }

    [Fact]
    public void Period_IsWholeDushanbeDays_AndThePeriodBeforeIsAsLong()
    {
        var (period, error) = AnalyticsPeriod.Parse(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7), new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero));

        Assert.Null(error);
        Assert.Equal(new DateTimeOffset(2026, 8, 31, 19, 0, 0, TimeSpan.Zero), period.StartUtc); // 00:00 Dushanbe
        Assert.Equal(new DateTimeOffset(2026, 9, 7, 19, 0, 0, TimeSpan.Zero), period.EndUtc);
        Assert.Equal(new AnalyticsPeriod(new DateOnly(2026, 8, 25), new DateOnly(2026, 8, 31)), period.Previous);
        Assert.Equal(7, period.Days);
    }

    [Fact]
    public void Period_DefaultsToTheLast30Days_EndingTodayInDushanbe()
    {
        // 20:00 UTC is already the next day in Dushanbe.
        var (period, _) = AnalyticsPeriod.Parse(null, null, new DateTimeOffset(2026, 9, 25, 20, 0, 0, TimeSpan.Zero));

        Assert.Equal(new AnalyticsPeriod(new DateOnly(2026, 8, 28), new DateOnly(2026, 9, 26)), period);
    }
}
