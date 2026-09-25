using System.Net;
using System.Text;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Office.Api.Channels;
using Office.Api.Channels.Automation;
using Office.Api.Channels.Comments;
using Office.Api.Channels.Flows;
using Office.Api.Channels.Instagram;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Features.CommentAutomation;
using Office.Api.Features.Flows;
using Office.Api.Realtime;

namespace Office.Api.Tests.Channels.Comments;

public class CommentsTests
{
    private readonly AppDbContext _db =
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private readonly RecordingCommentEventPublisher _events = new();

    private sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, string? Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request.Method, request.RequestUri!.ToString(), request.Content is null ? null : await request.Content.ReadAsStringAsync(ct)));
            return respond(request);
        }
    }

    private sealed class PassthroughProtector : IChannelCredentialsProtector
    {
        public string Protect(string plainText) => plainText;
        public string Unprotect(string protectedText) => protectedText;
    }

    private sealed class NoOpNotifications : INotificationService
    {
        public Task PushAsync(Guid userId, string type, object payload, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class NoJobs : Hangfire.IBackgroundJobClient
    {
        public string Create(Hangfire.Common.Job job, Hangfire.States.IState state) => throw new NotSupportedException();
        public bool ChangeState(string jobId, Hangfire.States.IState state, string? expectedState) => throw new NotSupportedException();
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private InstagramProvider Provider(FakeHttp http) => new(
        new HttpClient(http), new PassthroughProtector(), new ConfigurationBuilder().Build(), _db, new NoOpNotifications(),
        new MemoryCache(new MemoryCacheOptions()), new InstagramFollowCheckRateLimiter(), NullLogger<InstagramProvider>.Instance);

    private Channel AddChannel(Guid? owner = null, string externalId = "17841400000000001")
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(), Type = ChannelType.Instagram, Name = "shop", ExternalId = externalId, CustomerId = owner,
            CredentialsEncrypted = """{"instagramAccountId":"17841400000000001","accessToken":"tok"}""",
            IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.Channels.Add(channel);
        _db.SaveChanges();
        return channel;
    }

    private InstagramComment AddComment(Channel channel, string id = "c1", bool hidden = false, string? parent = null)
    {
        var comment = new InstagramComment
        {
            Id = Guid.NewGuid(), ChannelId = channel.Id, ExternalId = id, MediaExternalId = "m1", ParentExternalId = parent,
            AuthorExternalId = "fan", Text = "нарх?", CommentedAt = DateTimeOffset.UtcNow, ReceivedAt = DateTimeOffset.UtcNow, IsHidden = hidden,
        };
        _db.InstagramComments.Add(comment);
        _db.SaveChanges();
        return comment;
    }

    // ── CommentStore ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Store_KeepsAComment_Once_AndSignalsItsPost()
    {
        var channel = AddChannel();
        var store = new CommentStore(_db, _events, NullLogger<CommentStore>.Instance);
        var evt = new ParsedCommentEvent("c1", "fan-1", "fan_one", "нарх?", "m1", ParentId: null, OccurredAt: DateTimeOffset.UnixEpoch);

        Assert.True(await store.RecordAsync(channel, evt, CancellationToken.None));
        Assert.False(await store.RecordAsync(channel, evt, CancellationToken.None)); // Meta delivered it twice

        var stored = await _db.InstagramComments.SingleAsync();
        Assert.Equal((channel.Id, "m1", "fan_one", false, false), (stored.ChannelId, stored.MediaExternalId, stored.AuthorUsername, stored.IsOwn, stored.IsRead));
        Assert.Equal(DateTimeOffset.UnixEpoch, stored.CommentedAt);
        Assert.Equal([(channel.Id, "m1")], _events.Changed);
    }

    [Fact]
    public async Task Store_TheAccountsOwnComment_IsOwnAndAlreadyRead()
    {
        var channel = AddChannel();
        var store = new CommentStore(_db, _events, NullLogger<CommentStore>.Instance);

        await store.RecordAsync(channel, new ParsedCommentEvent("c2", channel.ExternalId, "shop", "ташаккур", "m1", "c1"), CancellationToken.None);

        var stored = await _db.InstagramComments.SingleAsync();
        Assert.True(stored.IsOwn);
        Assert.True(stored.IsRead);
        Assert.Equal("c1", stored.ParentExternalId);
    }

    [Fact]
    public async Task Store_SameMetaIdOnAnotherChannel_IsADifferentComment()
    {
        var a = AddChannel(externalId: "a");
        var b = AddChannel(externalId: "b");
        var store = new CommentStore(_db, _events, NullLogger<CommentStore>.Instance);
        var evt = new ParsedCommentEvent("same-id", "fan", null, "x", "m1");

        await store.RecordAsync(a, evt, CancellationToken.None);
        await store.RecordAsync(b, evt, CancellationToken.None);

        Assert.Equal(2, await _db.InstagramComments.CountAsync());
    }

    [Fact]
    public async Task Store_WithoutAPost_KeepsNothing()
    {
        var channel = AddChannel();
        var store = new CommentStore(_db, _events, NullLogger<CommentStore>.Instance);

        Assert.False(await store.RecordAsync(channel, new ParsedCommentEvent("c1", "fan", null, "x", MediaId: null), CancellationToken.None));
        Assert.Empty(await _db.InstagramComments.ToListAsync());
    }

    // ── Graph API parsing ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Parser_FlattensRepliesUnderTheirComment_AndReadsTheCursor()
    {
        const string body = """
            {
              "data": [
                { "id": "c1", "text": "нарх?", "timestamp": "2026-09-25T10:00:00+0000", "username": "fan_one", "from": { "id": "u1", "username": "fan_one" },
                  "replies": { "data": [ { "id": "r1", "text": "дар Direct", "from": { "id": "shop" }, "username": "shop", "hidden": false } ] } },
                { "id": "c2", "text": "spam", "from": { "id": "u2" }, "hidden": true },
                { "text": "no id — skipped" }
              ],
              "paging": { "cursors": { "after": "CURSOR" }, "next": "https://graph.instagram.com/..." }
            }
            """;

        var (items, next) = InstagramCommentParser.ParsePage(body);

        Assert.Equal(["c1", "r1", "c2"], items.Select(i => i.Id));
        Assert.Equal("c1", items[1].ParentId);
        Assert.True(items[2].Hidden);
        Assert.Equal(new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero), items[0].Timestamp);
        Assert.Equal("CURSOR", next);
        Assert.Equal("new-id", InstagramCommentParser.TryReadId("""{"id":"new-id"}"""));
        Assert.Null(InstagramCommentParser.TryReadId("not json"));
    }

    // ── Flows: the Direct they send ────────────────────────────────────────────────────────

    private FlowTriggerProcessor Trigger(FakeHttp http)
    {
        var engine = new FlowEngine(_db, Provider(http), new NoJobs(), new HttpClient(http), NullLogger<FlowEngine>.Instance);
        return new FlowTriggerProcessor(_db, engine, NullLogger<FlowTriggerProcessor>.Instance);
    }

    private void AddCommentFlow(Channel channel)
    {
        var flow = new Flow
        {
            Id = Guid.NewGuid(), ChannelId = channel.Id, Name = "f", IsActive = true, TriggerType = "instagram_comment",
            TriggerConfigJson = JsonSerializer.Serialize(new AutomationTriggerConfig("all", [], "all", [])),
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Flows.Add(flow);
        _db.FlowNodes.Add(new FlowNode
        {
            Id = Guid.NewGuid(), FlowId = flow.Id, Type = FlowNodeType.Message,
            ConfigJson = """{"Blocks":[{"Type":"text","Text":"Салом!","MediaId":null}],"Buttons":[]}""",
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task Flow_TheDirectItSends_IsMarkedOnTheComment()
    {
        var channel = AddChannel();
        AddCommentFlow(channel);
        AddComment(channel, "c1");
        var trigger = Trigger(new FakeHttp(_ => Json("""{"message_id":"mid"}""")));

        await trigger.ProcessCommentAsync(channel, new ParsedCommentEvent("c1", "fan", null, "нарх?", "m1"), CancellationToken.None);
        await _db.SaveChangesAsync();

        Assert.NotNull((await _db.InstagramComments.SingleAsync()).PrivateReplySentAt);
    }

    // ── The comment auto-reply (CommentAutomationJob) ──────────────────────────────────────

    private const string FollowersReply = "Ташаккур! 🙌";
    private const string AskToFollowReply = "Аввал обуна шавед 🙏";

    /// <summary>Instagram, faked: the follow check answers <paramref name="following"/>; a reply or a Direct message succeeds.</summary>
    private static FakeHttp Instagram(bool following = true) => new(request =>
        request.Method == HttpMethod.Get && request.RequestUri!.Query.Contains("is_user_follow_business")
            ? Json($$"""{"username":"fan","is_user_follow_business":{{(following ? "true" : "false")}}}""")
            : request.RequestUri!.AbsolutePath.EndsWith("/replies")
                ? Json("""{"id":"reply-1"}""")
                : Json("""{"recipient_id":"fan","message_id":"mid"}"""));

    private CommentAutomationJob Job(FakeHttp http) => new(_db, Provider(http), _events, NullLogger<CommentAutomationJob>.Instance);

    private AutomationRule AddRule(Channel channel, bool requiresFollow = false, string dmText = "", int cooldownMinutes = 60)
    {
        var rule = new AutomationRule
        {
            Id = Guid.NewGuid(), ChannelId = channel.Id, Name = "r", IsActive = true, TriggerType = "instagram_comment",
            TriggerConfigJson = JsonSerializer.Serialize(new AutomationTriggerConfig("all", [], "all", [])),
            ConditionConfigJson = JsonSerializer.Serialize(new AutomationConditionConfig(requiresFollow)),
            ActionConfigJson = JsonSerializer.Serialize(new AutomationActionConfig(
                new AutomationReplyAction([FollowersReply], dmText, null),
                requiresFollow ? new AutomationReplyAction([AskToFollowReply], "Обуна шавед ва аз нав шарҳ нависед", null) : null)),
            CooldownMinutes = cooldownMinutes, CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.AutomationRules.Add(rule);
        _db.SaveChanges();
        return rule;
    }

    private AutomationRun AddRun(AutomationRule rule, string commentId = "c1", int minutesAgo = 0,
        FollowCheckResult? followCheck = null, AutomationRunStatus status = AutomationRunStatus.Pending)
    {
        var run = new AutomationRun
        {
            Id = Guid.NewGuid(), RuleId = rule.Id, TriggerExternalId = commentId, ActorExternalId = "fan", TargetMediaExternalId = "m1",
            FollowCheckResult = followCheck, CommentReplyStatus = status, DmStatus = status,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo),
        };
        _db.AutomationRuns.Add(run);
        _db.SaveChanges();
        return run;
    }

    private static bool IsReply((HttpMethod Method, string Url, string? Body) call) => call.Method == HttpMethod.Post && call.Url.Contains("/replies");

    [Fact]
    public async Task Rule_PostsTheReply_StoresItOnTheCommentsPage_AndSignalsThePost()
    {
        var channel = AddChannel();
        AddComment(channel, "c1");
        var run = AddRun(AddRule(channel));
        var http = Instagram();

        await Job(http).RunAsync(run.Id, CancellationToken.None);

        var call = Assert.Single(http.Calls);
        Assert.True(IsReply(call));
        Assert.EndsWith("/c1/replies", call.Url);
        var reply = await _db.InstagramComments.SingleAsync(c => c.ExternalId == "reply-1");
        Assert.True(reply.IsOwn && reply.PostedByAutomation && reply.IsRead);
        Assert.Equal(("c1", FollowersReply), (reply.ParentExternalId, reply.Text));
        Assert.Equal(AutomationRunStatus.Sent, run.CommentReplyStatus);
        Assert.Equal([(channel.Id, "m1")], _events.Changed);
    }

    [Fact]
    public async Task Rule_ReplyToAReply_GoesUnderTheTopComment_TheDirectToTheCommenter()
    {
        var channel = AddChannel();
        AddComment(channel, "c1");
        AddComment(channel, "c2", parent: "c1");
        var run = AddRun(AddRule(channel, dmText: "Нарх дар Direct"), commentId: "c2");
        var http = Instagram();

        await Job(http).RunAsync(run.Id, CancellationToken.None);

        Assert.EndsWith("/c1/replies", Assert.Single(http.Calls, IsReply).Url);
        var direct = Assert.Single(http.Calls, c => !IsReply(c));
        Assert.Contains("\"comment_id\":\"c2\"", direct.Body);
        Assert.NotNull((await _db.InstagramComments.SingleAsync(c => c.ExternalId == "c2")).PrivateReplySentAt);
    }

    [Fact]
    public async Task Rule_WhenInstagramRefuses_KeepsTheReasonOnTheComment()
    {
        var channel = AddChannel();
        AddComment(channel, "c1");
        var run = AddRun(AddRule(channel));
        var http = new FakeHttp(_ => Json("""{"error":{"message":"Unsupported post request","code":100}}""", HttpStatusCode.BadRequest));

        await Job(http).RunAsync(run.Id, CancellationToken.None);

        Assert.Equal(AutomationRunStatus.Failed, run.CommentReplyStatus);
        Assert.False(string.IsNullOrEmpty((await _db.InstagramComments.SingleAsync()).AutoReplyError));
    }

    [Fact]
    public async Task Rule_MizojWithoutAPlan_SendsNothing()
    {
        var owner = new Customer
        {
            Id = Guid.NewGuid(), Email = "m@example.com", FullName = "M", TrialEndsAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        _db.Customers.Add(owner);
        var channel = AddChannel(owner.Id);
        AddComment(channel, "c1");
        var run = AddRun(AddRule(channel, dmText: "Нарх дар Direct"));
        var http = Instagram();

        await Job(http).RunAsync(run.Id, CancellationToken.None);

        Assert.Empty(http.Calls);
        Assert.Equal((AutomationRunStatus.Disabled, AutomationRunStatus.Disabled), (run.CommentReplyStatus, run.DmStatus));
    }

    [Fact]
    public async Task Rule_EchoAlreadyStored_IsMarkedNotDuplicated()
    {
        var channel = AddChannel();
        AddComment(channel, "c1");
        var echo = AddComment(channel, "reply-1", parent: "c1");
        echo.IsOwn = true;
        _db.SaveChanges();
        var run = AddRun(AddRule(channel));

        await Job(Instagram()).RunAsync(run.Id, CancellationToken.None);

        Assert.Equal(2, await _db.InstagramComments.CountAsync());
        Assert.True((await _db.InstagramComments.SingleAsync(c => c.ExternalId == "reply-1")).PostedByAutomation);
    }

    [Fact]
    public async Task Rule_NotFollowing_IsAskedToFollow()
    {
        var channel = AddChannel();
        AddComment(channel, "c1");
        var run = AddRun(AddRule(channel, requiresFollow: true));

        await Job(Instagram(following: false)).RunAsync(run.Id, CancellationToken.None);

        Assert.Equal(FollowCheckResult.NotFollowing, run.FollowCheckResult);
        Assert.Equal(AskToFollowReply, (await _db.InstagramComments.SingleAsync(c => c.ExternalId == "reply-1")).Text);
    }

    [Fact]
    public async Task Rule_FollowedAfterBeingAsked_CommentsAgain_GetsTheFollowersReply()
    {
        var channel = AddChannel();
        var rule = AddRule(channel, requiresFollow: true);
        AddRun(rule, commentId: "c0", minutesAgo: 5, FollowCheckResult.NotFollowing, AutomationRunStatus.Sent);
        AddComment(channel, "c1");
        var run = AddRun(rule, commentId: "c1");

        await Job(Instagram(following: true)).RunAsync(run.Id, CancellationToken.None);

        Assert.Equal(AutomationRunStatus.Sent, run.CommentReplyStatus);
        Assert.Equal(FollowersReply, (await _db.InstagramComments.SingleAsync(c => c.ExternalId == "reply-1")).Text);
    }

    [Fact]
    public async Task Rule_StillNotFollowingAfterBeingAsked_IsNotAskedAgain()
    {
        var channel = AddChannel();
        var rule = AddRule(channel, requiresFollow: true);
        AddRun(rule, commentId: "c0", minutesAgo: 5, FollowCheckResult.NotFollowing, AutomationRunStatus.Sent);
        AddComment(channel, "c1");
        var run = AddRun(rule, commentId: "c1");
        var http = Instagram(following: false);

        await Job(http).RunAsync(run.Id, CancellationToken.None);

        Assert.Equal(AutomationRunStatus.SkippedCooldown, run.CommentReplyStatus);
        Assert.DoesNotContain(http.Calls, c => c.Method == HttpMethod.Post); // only the follow check
    }

    // ── Validation ────────────────────────────────────────────────────────────────────────

    private static CreateAutomationRuleRequest Rule(
        string[]? keywords = null, string[]? postIds = null, string[]? replies = null, string dmText = "", string? buttonUrl = null,
        int cooldown = 60) => new(
        "r",
        new AutomationTriggerConfig(keywords is null ? "all" : "keyword", keywords ?? [], postIds is null ? "all" : "selected", postIds ?? []),
        new AutomationConditionConfig(false),
        new AutomationActionConfig(new AutomationReplyAction(replies ?? ["Ташаккур!"], dmText, buttonUrl, buttonUrl is null ? null : "Сайт"), null),
        cooldown);

    private static bool Valid(CreateAutomationRuleRequest request) => new CreateAutomationRuleRequestValidator().Validate(request).IsValid;

    [Fact]
    public void RuleLimits_EveryListAndTextHasACeiling()
    {
        Assert.True(Valid(Rule()));
        Assert.True(Valid(Rule(keywords: Enumerable.Repeat("нарх", AutomationRuleLimits.MaxKeywords).ToArray())));
        Assert.False(Valid(Rule(keywords: Enumerable.Repeat("нарх", AutomationRuleLimits.MaxKeywords + 1).ToArray())));
        Assert.False(Valid(Rule(keywords: [new string('k', AutomationRuleLimits.MaxKeywordLength + 1)])));
        Assert.True(Valid(Rule(postIds: ["17900000000000001", "123_456"])));
        Assert.False(Valid(Rule(postIds: ["../me/messages"])));
        Assert.False(Valid(Rule(replies: Enumerable.Repeat("x", AutomationRuleLimits.MaxCommentReplies + 1).ToArray())));
        Assert.False(Valid(Rule(replies: [new string('x', AutomationRuleLimits.MaxTextLength + 1)])));
        Assert.False(Valid(Rule(dmText: new string('x', AutomationRuleLimits.MaxTextLength + 1))));
        Assert.True(Valid(Rule(dmText: "Салом", buttonUrl: "https://nizom.tj")));
        Assert.False(Valid(Rule(dmText: "Салом", buttonUrl: "javascript:alert(1)")));
        Assert.False(Valid(Rule(cooldown: AutomationRuleLimits.MaxCooldownMinutes + 1)));
    }

    [Fact]
    public void DryRun_TheUserIdGoesIntoAGraphPath_SoOnlyDigits()
    {
        static bool DryRunValid(string? actor) => new DryRunAutomationRuleRequestValidator().Validate(
            new DryRunAutomationRuleRequest(new AutomationTriggerConfig("all", [], "all", []), "нарх?", null,
                new AutomationConditionConfig(true), actor)).IsValid;

        Assert.True(DryRunValid(null));
        Assert.True(DryRunValid("1254001234567890"));
        Assert.False(DryRunValid("me/conversations?fields=messages"));
        Assert.False(DryRunValid("../123"));
    }
}
