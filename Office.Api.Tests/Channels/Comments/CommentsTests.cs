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

    // ── Public replies in flows ────────────────────────────────────────────────────────────

    private (FlowTriggerProcessor, RecordingPublicReplyScheduler) Trigger(FakeHttp http)
    {
        var scheduler = new RecordingPublicReplyScheduler();
        var engine = new FlowEngine(_db, Provider(http), new NoJobs(), new HttpClient(http), NullLogger<FlowEngine>.Instance);
        return (new FlowTriggerProcessor(_db, engine, scheduler, NullLogger<FlowTriggerProcessor>.Instance), scheduler);
    }

    private Flow AddCommentFlow(Channel channel, string[]? publicReplies, string triggerType = "instagram_comment")
    {
        var flow = new Flow
        {
            Id = Guid.NewGuid(), ChannelId = channel.Id, Name = "f", IsActive = true, TriggerType = triggerType,
            TriggerConfigJson = JsonSerializer.Serialize(new AutomationTriggerConfig("all", [], "all", [], publicReplies)),
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Flows.Add(flow);
        _db.FlowNodes.Add(new FlowNode
        {
            Id = Guid.NewGuid(), FlowId = flow.Id, Type = FlowNodeType.Message,
            ConfigJson = """{"Blocks":[{"Type":"text","Text":"Салом!","MediaId":null}],"Buttons":[]}""",
        });
        _db.SaveChanges();
        return flow;
    }

    [Fact]
    public async Task Flow_WithPublicReplies_SchedulesThemInTurn()
    {
        var channel = AddChannel();
        AddCommentFlow(channel, ["Ба Direct навиштем 📩", "Direct-ро санҷед ✉️"]);
        var (trigger, scheduler) = Trigger(new FakeHttp(_ => Json("""{"message_id":"mid"}""")));

        foreach (var id in new[] { "c1", "c2", "c3" })
            await trigger.ProcessCommentAsync(channel, new ParsedCommentEvent(id, $"fan-{id}", null, "нарх?", "m1"), CancellationToken.None);

        Assert.Equal(
            [(channel.Id, "c1", "Ба Direct навиштем 📩"), (channel.Id, "c2", "Direct-ро санҷед ✉️"), (channel.Id, "c3", "Ба Direct навиштем 📩")],
            scheduler.Scheduled);
    }

    [Fact]
    public async Task Flow_WithoutPublicReplies_SchedulesNone()
    {
        var channel = AddChannel();
        AddCommentFlow(channel, publicReplies: null);
        var (trigger, scheduler) = Trigger(new FakeHttp(_ => Json("""{"message_id":"mid"}""")));

        await trigger.ProcessCommentAsync(channel, new ParsedCommentEvent("c1", "fan", null, "нарх?", "m1"), CancellationToken.None);

        Assert.Empty(scheduler.Scheduled);
    }

    [Fact]
    public async Task Flow_TheDirectItSends_IsMarkedOnTheComment()
    {
        var channel = AddChannel();
        AddCommentFlow(channel, publicReplies: null);
        AddComment(channel, "c1");
        var (trigger, _) = Trigger(new FakeHttp(_ => Json("""{"message_id":"mid"}""")));

        await trigger.ProcessCommentAsync(channel, new ParsedCommentEvent("c1", "fan", null, "нарх?", "m1"), CancellationToken.None);
        await _db.SaveChangesAsync();

        Assert.NotNull((await _db.InstagramComments.SingleAsync()).PrivateReplySentAt);
    }

    // ── CommentPublicReplyJob ──────────────────────────────────────────────────────────────

    private CommentPublicReplyJob Job(FakeHttp http) => new(_db, Provider(http), _events, NullLogger<CommentPublicReplyJob>.Instance);

    [Fact]
    public async Task Job_PostsTheReply_AndStoresItAsTheAccountsOwn()
    {
        var channel = AddChannel();
        AddComment(channel, "c1");
        var http = new FakeHttp(_ => Json("""{"id":"reply-1"}"""));

        await Job(http).RunAsync(channel.Id, "c1", "Ба Direct навиштем 📩", CancellationToken.None);

        var call = Assert.Single(http.Calls);
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.EndsWith("/c1/replies", call.Url);
        var reply = await _db.InstagramComments.SingleAsync(c => c.ExternalId == "reply-1");
        Assert.True(reply.IsOwn && reply.PostedByAutomation && reply.IsRead);
        Assert.Equal("c1", reply.ParentExternalId);
        Assert.Equal([(channel.Id, "m1")], _events.Changed);
    }

    [Fact]
    public async Task Job_ReplyToAReply_GoesUnderTheTopComment()
    {
        var channel = AddChannel();
        AddComment(channel, "c1");
        AddComment(channel, "c2", parent: "c1");
        var http = new FakeHttp(_ => Json("""{"id":"reply-1"}"""));

        await Job(http).RunAsync(channel.Id, "c2", "📩", CancellationToken.None);

        Assert.EndsWith("/c1/replies", Assert.Single(http.Calls).Url);
    }

    [Fact]
    public async Task Job_WhenInstagramRefuses_KeepsTheReasonOnTheComment()
    {
        var channel = AddChannel();
        AddComment(channel, "c1");
        var http = new FakeHttp(_ => Json("""{"error":{"message":"Unsupported post request","code":100}}""", HttpStatusCode.BadRequest));

        await Job(http).RunAsync(channel.Id, "c1", "📩", CancellationToken.None);

        var comment = await _db.InstagramComments.SingleAsync();
        Assert.False(string.IsNullOrEmpty(comment.AutoReplyError));
    }

    [Fact]
    public async Task Job_HiddenComment_GetsNoReply()
    {
        var channel = AddChannel();
        AddComment(channel, "c1", hidden: true);
        var http = new FakeHttp(_ => Json("""{"id":"reply-1"}"""));

        await Job(http).RunAsync(channel.Id, "c1", "📩", CancellationToken.None);

        Assert.Empty(http.Calls);
    }

    [Fact]
    public async Task Job_MizojWithoutAPlan_GetsNoReply()
    {
        var owner = new Customer
        {
            Id = Guid.NewGuid(), Email = "m@example.com", FullName = "M", TrialEndsAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        _db.Customers.Add(owner);
        var channel = AddChannel(owner.Id);
        AddComment(channel, "c1");
        var http = new FakeHttp(_ => Json("""{"id":"reply-1"}"""));

        await Job(http).RunAsync(channel.Id, "c1", "📩", CancellationToken.None);

        Assert.Empty(http.Calls);
    }

    [Fact]
    public async Task Job_EchoAlreadyStored_IsMarkedNotDuplicated()
    {
        var channel = AddChannel();
        AddComment(channel, "c1");
        var echo = AddComment(channel, "reply-1", parent: "c1");
        echo.IsOwn = true;
        _db.SaveChanges();

        await Job(new FakeHttp(_ => Json("""{"id":"reply-1"}"""))).RunAsync(channel.Id, "c1", "📩", CancellationToken.None);

        Assert.Equal(2, await _db.InstagramComments.CountAsync());
        Assert.True((await _db.InstagramComments.SingleAsync(c => c.ExternalId == "reply-1")).PostedByAutomation);
    }

    // ── Validation ────────────────────────────────────────────────────────────────────────

    private static bool Valid(string triggerType, string[]? replies) => new CreateFlowRequestValidator()
        .Validate(new CreateFlowRequest("f", triggerType, new AutomationTriggerConfig("all", [], "all", [], replies))).IsValid;

    [Fact]
    public void PublicReplies_OnlyOnACommentTrigger_UpToFiveShortOnes()
    {
        Assert.True(Valid("instagram_comment", null));
        Assert.True(Valid("instagram_comment", ["a", "b", "c", "d", "e"]));
        Assert.True(Valid("instagram_comment", [new string('x', 300)]));
        Assert.False(Valid("instagram_comment", ["a", "b", "c", "d", "e", "f"]));
        Assert.False(Valid("instagram_comment", ["  "]));
        Assert.False(Valid("instagram_comment", [new string('x', 301)]));
        Assert.False(Valid("instagram_dm", ["a"]));
        Assert.True(Valid("instagram_dm", []));
    }
}
