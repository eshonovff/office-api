using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Office.Api.Channels.Automation;
using Office.Api.Channels.Instagram;
using Office.Api.Data;
using Office.Api.Data.Entities;

namespace Office.Api.Tests.Integration;

/// <summary>
/// CommentAutomationProcessor бо DB воқеӣ (EF Core InMemory, ҳамон алгуи WebhookProcessorRealtimeTests) —
/// филтри ҳалқа, интихоби аввалин rule-и мувофиқ, ва cooldown-ро аз рӯи DB-и воқеӣ месанҷад
/// (на pure — ин синф LINQ-и воқеӣ мезанад, пас fake DbContext кофӣ нест).
/// </summary>
public class CommentAutomationProcessorTests
{
    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Channel MakeChannel(string externalId) => new()
    {
        Id = Guid.CreateVersion7(),
        Type = ChannelType.Instagram,
        Name = "Test IG channel",
        ExternalId = externalId,
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static AutomationRule MakeRule(Guid channelId, int cooldownMinutes = 0, string matchMode = AutomationTriggerConfig.MatchModeAll) => new()
    {
        Id = Guid.CreateVersion7(),
        ChannelId = channelId,
        Name = "Test rule",
        IsActive = true,
        TriggerType = "instagram_comment",
        TriggerConfigJson = System.Text.Json.JsonSerializer.Serialize(
            new AutomationTriggerConfig(matchMode, ["нарх"], AutomationTriggerConfig.PostScopeAll, [])),
        ActionConfigJson = System.Text.Json.JsonSerializer.Serialize(
            new AutomationActionConfig(new AutomationReplyAction(["Ташаккур!"], "Салом дар DM", null), null)),
        CooldownMinutes = cooldownMinutes,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>Job-ро воқеан enqueue намекунад — санҷиши ин синф ба Meta/Hangfire ниёз надорад.</summary>
    private sealed class RecordingBackgroundJobClient : Hangfire.IBackgroundJobClient
    {
        public List<string> EnqueuedMethods { get; } = [];

        public string Create(Job job, IState state)
        {
            EnqueuedMethods.Add(job.Method.Name);
            return Guid.NewGuid().ToString();
        }

        public bool ChangeState(string jobId, IState state, string? expectedState) => throw new NotSupportedException();
    }

    [Fact]
    public async Task ProcessAsync_ActorIsTheBusinessAccountItself_SkipsWithoutRecordingRun()
    {
        await using var db = CreateDb();
        var channel = MakeChannel("17841437397996064");
        db.Channels.Add(channel);
        db.AutomationRules.Add(MakeRule(channel.Id));
        await db.SaveChangesAsync();

        var backgroundJobs = new RecordingBackgroundJobClient();
        var processor = new CommentAutomationProcessor(db, backgroundJobs, NullLogger<CommentAutomationProcessor>.Instance);

        var evt = new ParsedCommentEvent("comment-1", channel.ExternalId, "self", "нарх?", "media-1");
        await processor.ProcessAsync(channel, evt, CancellationToken.None);

        Assert.Empty(await db.AutomationRuns.ToListAsync());
        Assert.Empty(backgroundJobs.EnqueuedMethods);
    }

    [Fact]
    public async Task ProcessAsync_MatchingComment_RecordsPendingRunAndEnqueuesJob()
    {
        await using var db = CreateDb();
        var channel = MakeChannel("17841437397996064");
        db.Channels.Add(channel);
        db.AutomationRules.Add(MakeRule(channel.Id));
        await db.SaveChangesAsync();

        var backgroundJobs = new RecordingBackgroundJobClient();
        var processor = new CommentAutomationProcessor(db, backgroundJobs, NullLogger<CommentAutomationProcessor>.Instance);

        var evt = new ParsedCommentEvent("comment-1", "actor-1", "someone", "🔥", "media-1");
        await processor.ProcessAsync(channel, evt, CancellationToken.None);

        var run = Assert.Single(await db.AutomationRuns.ToListAsync());
        Assert.Equal(AutomationRunStatus.Pending, run.CommentReplyStatus);
        Assert.Equal(AutomationRunStatus.Pending, run.DmStatus);
        Assert.Equal(nameof(CommentAutomationJob.RunAsync), Assert.Single(backgroundJobs.EnqueuedMethods));
    }

    [Fact]
    public async Task ProcessAsync_NoRuleMatches_RecordsNothing()
    {
        await using var db = CreateDb();
        var channel = MakeChannel("17841437397996064");
        db.Channels.Add(channel);
        db.AutomationRules.Add(MakeRule(channel.Id, matchMode: AutomationTriggerConfig.MatchModeKeyword));
        await db.SaveChangesAsync();

        var backgroundJobs = new RecordingBackgroundJobClient();
        var processor = new CommentAutomationProcessor(db, backgroundJobs, NullLogger<CommentAutomationProcessor>.Instance);

        // "нарх" калимаи калидист (ниг. MakeRule) — ин матн онро надорад.
        var evt = new ParsedCommentEvent("comment-1", "actor-1", "someone", "🔥👏", "media-1");
        await processor.ProcessAsync(channel, evt, CancellationToken.None);

        Assert.Empty(await db.AutomationRuns.ToListAsync());
        Assert.Empty(backgroundJobs.EnqueuedMethods);
    }

    [Fact]
    public async Task ProcessAsync_SameActorSamePostWithinCooldown_SkipsAndRecordsSkippedCooldown()
    {
        await using var db = CreateDb();
        var channel = MakeChannel("17841437397996064");
        var rule = MakeRule(channel.Id, cooldownMinutes: 60);
        db.Channels.Add(channel);
        db.AutomationRules.Add(rule);
        db.AutomationRuns.Add(new AutomationRun
        {
            Id = Guid.CreateVersion7(),
            RuleId = rule.Id,
            TriggerExternalId = "comment-0",
            ActorExternalId = "actor-1",
            TargetMediaExternalId = "media-1",
            CommentReplyStatus = AutomationRunStatus.Sent,
            DmStatus = AutomationRunStatus.Sent,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
        });
        await db.SaveChangesAsync();

        var backgroundJobs = new RecordingBackgroundJobClient();
        var processor = new CommentAutomationProcessor(db, backgroundJobs, NullLogger<CommentAutomationProcessor>.Instance);

        var evt = new ParsedCommentEvent("comment-1", "actor-1", "someone", "🔥", "media-1");
        await processor.ProcessAsync(channel, evt, CancellationToken.None);

        var newRun = Assert.Single(await db.AutomationRuns.Where(r => r.TriggerExternalId == "comment-1").ToListAsync());
        Assert.Equal(AutomationRunStatus.SkippedCooldown, newRun.CommentReplyStatus);
        Assert.Empty(backgroundJobs.EnqueuedMethods);
    }

    [Fact]
    public async Task ProcessAsync_SameActorDifferentPost_CooldownDoesNotApply()
    {
        await using var db = CreateDb();
        var channel = MakeChannel("17841437397996064");
        var rule = MakeRule(channel.Id, cooldownMinutes: 60);
        db.Channels.Add(channel);
        db.AutomationRules.Add(rule);
        db.AutomationRuns.Add(new AutomationRun
        {
            Id = Guid.CreateVersion7(),
            RuleId = rule.Id,
            TriggerExternalId = "comment-0",
            ActorExternalId = "actor-1",
            TargetMediaExternalId = "media-OTHER",
            CommentReplyStatus = AutomationRunStatus.Sent,
            DmStatus = AutomationRunStatus.Sent,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
        });
        await db.SaveChangesAsync();

        var backgroundJobs = new RecordingBackgroundJobClient();
        var processor = new CommentAutomationProcessor(db, backgroundJobs, NullLogger<CommentAutomationProcessor>.Instance);

        var evt = new ParsedCommentEvent("comment-1", "actor-1", "someone", "🔥", "media-1");
        await processor.ProcessAsync(channel, evt, CancellationToken.None);

        var newRun = Assert.Single(await db.AutomationRuns.Where(r => r.TriggerExternalId == "comment-1").ToListAsync());
        Assert.Equal(AutomationRunStatus.Pending, newRun.CommentReplyStatus);
        Assert.Single(backgroundJobs.EnqueuedMethods);
    }

    [Fact]
    public async Task ProcessAsync_AfterCooldownExpires_ProceedsNormally()
    {
        await using var db = CreateDb();
        var channel = MakeChannel("17841437397996064");
        var rule = MakeRule(channel.Id, cooldownMinutes: 5);
        db.Channels.Add(channel);
        db.AutomationRules.Add(rule);
        db.AutomationRuns.Add(new AutomationRun
        {
            Id = Guid.CreateVersion7(),
            RuleId = rule.Id,
            TriggerExternalId = "comment-0",
            ActorExternalId = "actor-1",
            TargetMediaExternalId = "media-1",
            CommentReplyStatus = AutomationRunStatus.Sent,
            DmStatus = AutomationRunStatus.Sent,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
        });
        await db.SaveChangesAsync();

        var backgroundJobs = new RecordingBackgroundJobClient();
        var processor = new CommentAutomationProcessor(db, backgroundJobs, NullLogger<CommentAutomationProcessor>.Instance);

        var evt = new ParsedCommentEvent("comment-1", "actor-1", "someone", "🔥", "media-1");
        await processor.ProcessAsync(channel, evt, CancellationToken.None);

        var newRun = Assert.Single(await db.AutomationRuns.Where(r => r.TriggerExternalId == "comment-1").ToListAsync());
        Assert.Equal(AutomationRunStatus.Pending, newRun.CommentReplyStatus);
        Assert.Single(backgroundJobs.EnqueuedMethods);
    }

    [Fact]
    public async Task ProcessAsync_InactiveRule_IsIgnored()
    {
        await using var db = CreateDb();
        var channel = MakeChannel("17841437397996064");
        var rule = MakeRule(channel.Id);
        rule.IsActive = false;
        db.Channels.Add(channel);
        db.AutomationRules.Add(rule);
        await db.SaveChangesAsync();

        var backgroundJobs = new RecordingBackgroundJobClient();
        var processor = new CommentAutomationProcessor(db, backgroundJobs, NullLogger<CommentAutomationProcessor>.Instance);

        var evt = new ParsedCommentEvent("comment-1", "actor-1", "someone", "🔥", "media-1");
        await processor.ProcessAsync(channel, evt, CancellationToken.None);

        Assert.Empty(await db.AutomationRuns.ToListAsync());
    }

    [Fact]
    public async Task ProcessAsync_CommentAlreadyProcessed_DoesNotCreateASecondRun()
    {
        // Meta метавонад ҳамон webhook-ро такрор фиристад (масалан ҷавоби мо дар вақти
        // муайяншуда нарасид) — идемпотентии comment_id ҳимоя мекунад, то follow-check/private
        // reply дубора иҷро нашавад (Meta барои як comment_id танҳо ЯК private reply иҷозат медиҳад).
        await using var db = CreateDb();
        var channel = MakeChannel("17841437397996064");
        var rule = MakeRule(channel.Id);
        db.Channels.Add(channel);
        db.AutomationRules.Add(rule);
        db.AutomationRuns.Add(new AutomationRun
        {
            Id = Guid.CreateVersion7(),
            RuleId = rule.Id,
            TriggerExternalId = "comment-1",
            ActorExternalId = "actor-1",
            TargetMediaExternalId = "media-1",
            CommentReplyStatus = AutomationRunStatus.Sent,
            DmStatus = AutomationRunStatus.Sent,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var backgroundJobs = new RecordingBackgroundJobClient();
        var processor = new CommentAutomationProcessor(db, backgroundJobs, NullLogger<CommentAutomationProcessor>.Instance);

        var evt = new ParsedCommentEvent("comment-1", "actor-1", "someone", "🔥", "media-1");
        await processor.ProcessAsync(channel, evt, CancellationToken.None);

        Assert.Single(await db.AutomationRuns.ToListAsync());
        Assert.Empty(backgroundJobs.EnqueuedMethods);
    }

    private static AutomationRun PastRun(AutomationRule rule, string commentId, int minutesAgo, AutomationRunStatus status,
        FollowCheckResult? followCheck = null) => new()
    {
        Id = Guid.CreateVersion7(),
        RuleId = rule.Id,
        TriggerExternalId = commentId,
        ActorExternalId = "actor-1",
        TargetMediaExternalId = "media-1",
        FollowCheckResult = followCheck,
        CommentReplyStatus = status,
        DmStatus = status,
        CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo),
    };

    private static async Task<AutomationRun> ProcessNewComment(AppDbContext db, Channel channel, RecordingBackgroundJobClient jobs)
    {
        var processor = new CommentAutomationProcessor(db, jobs, NullLogger<CommentAutomationProcessor>.Instance);
        await processor.ProcessAsync(channel, new ParsedCommentEvent("comment-new", "actor-1", "someone", "🔥", "media-1"), CancellationToken.None);
        return await db.AutomationRuns.SingleAsync(r => r.TriggerExternalId == "comment-new");
    }

    [Fact]
    public async Task ProcessAsync_CommentAgainAfterBeingAskedToFollow_IsLetThroughTheCooldown()
    {
        // "Follow us, then comment again" — this is that comment; the job checks the follow afresh.
        await using var db = CreateDb();
        var channel = MakeChannel("17841437397996064");
        var rule = MakeRule(channel.Id, cooldownMinutes: 60);
        db.Channels.Add(channel);
        db.AutomationRules.Add(rule);
        db.AutomationRuns.Add(PastRun(rule, "comment-0", 5, AutomationRunStatus.Sent, FollowCheckResult.NotFollowing));
        await db.SaveChangesAsync();
        var jobs = new RecordingBackgroundJobClient();

        var run = await ProcessNewComment(db, channel, jobs);

        Assert.Equal(AutomationRunStatus.Pending, run.CommentReplyStatus);
        Assert.Single(jobs.EnqueuedMethods);
    }

    [Fact]
    public async Task ProcessAsync_CommentsSkippedForTheCooldown_DoNotExtendIt()
    {
        // Replied 70 minutes ago (cooldown 60); a comment 10 minutes ago was skipped. The cooldown
        // runs from the reply, so this one is answered — or someone commenting every half hour
        // would never be answered again.
        await using var db = CreateDb();
        var channel = MakeChannel("17841437397996064");
        var rule = MakeRule(channel.Id, cooldownMinutes: 60);
        db.Channels.Add(channel);
        db.AutomationRules.Add(rule);
        db.AutomationRuns.AddRange(
            PastRun(rule, "comment-0", 70, AutomationRunStatus.Sent),
            PastRun(rule, "comment-1", 10, AutomationRunStatus.SkippedCooldown));
        await db.SaveChangesAsync();
        var jobs = new RecordingBackgroundJobClient();

        var run = await ProcessNewComment(db, channel, jobs);

        Assert.Equal(AutomationRunStatus.Pending, run.CommentReplyStatus);
        Assert.Single(jobs.EnqueuedMethods);
    }

    [Fact]
    public async Task ProcessAsync_MizojWithoutAPlan_RecordsNothing()
    {
        await using var db = CreateDb();
        var owner = new Customer { Id = Guid.CreateVersion7(), Email = "m@example.com", FullName = "M", TrialEndsAt = DateTimeOffset.UtcNow.AddDays(-1) };
        var channel = MakeChannel("17841437397996064");
        channel.CustomerId = owner.Id;
        db.Customers.Add(owner);
        db.Channels.Add(channel);
        db.AutomationRules.Add(MakeRule(channel.Id));
        await db.SaveChangesAsync();
        var jobs = new RecordingBackgroundJobClient();
        var processor = new CommentAutomationProcessor(db, jobs, NullLogger<CommentAutomationProcessor>.Instance);

        await processor.ProcessAsync(channel, new ParsedCommentEvent("comment-1", "actor-1", "someone", "нарх?", "media-1"), CancellationToken.None);

        Assert.Empty(await db.AutomationRuns.ToListAsync());
        Assert.Empty(jobs.EnqueuedMethods);
    }

    [Fact]
    public async Task ProcessAsync_MizojInTrial_Runs()
    {
        await using var db = CreateDb();
        var owner = new Customer { Id = Guid.CreateVersion7(), Email = "m@example.com", FullName = "M", TrialEndsAt = DateTimeOffset.UtcNow.AddDays(3) };
        var channel = MakeChannel("17841437397996064");
        channel.CustomerId = owner.Id;
        db.Customers.Add(owner);
        db.Channels.Add(channel);
        db.AutomationRules.Add(MakeRule(channel.Id));
        await db.SaveChangesAsync();
        var jobs = new RecordingBackgroundJobClient();
        var processor = new CommentAutomationProcessor(db, jobs, NullLogger<CommentAutomationProcessor>.Instance);

        await processor.ProcessAsync(channel, new ParsedCommentEvent("comment-1", "actor-1", "someone", "нарх?", "media-1"), CancellationToken.None);

        Assert.Single(await db.AutomationRuns.ToListAsync());
        Assert.Single(jobs.EnqueuedMethods);
    }
}

