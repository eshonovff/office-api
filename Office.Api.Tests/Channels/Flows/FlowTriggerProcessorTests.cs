using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Office.Api.Channels;
using Office.Api.Channels.Flows;
using Office.Api.Channels.Instagram;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Realtime;

namespace Office.Api.Tests.Channels.Flows;

public class FlowTriggerProcessorTests
{
    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Channel MakeChannel(string externalId = "17841400000000000") => new()
    {
        Id = Guid.CreateVersion7(),
        Type = ChannelType.Instagram,
        Name = "Test IG channel",
        ExternalId = externalId,
        CredentialsEncrypted = """{"instagramAccountId":"17841400000000000","accessToken":"tok"}""",
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static Flow MakeFlow(Guid channelId, string triggerType, string matchMode = "all", string[]? keywords = null) => new()
    {
        Id = Guid.CreateVersion7(),
        ChannelId = channelId,
        Name = "Test flow",
        IsActive = true,
        TriggerType = triggerType,
        TriggerConfigJson = $$"""{"MatchMode":"{{matchMode}}","Keywords":[{{string.Join(",", (keywords ?? []).Select(k => $"\"{k}\""))}}],"PostScope":"all","PostIds":[]}""",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static FlowNode MakeNode(Guid flowId, string text = "Салом!") => new()
    {
        Id = Guid.CreateVersion7(),
        FlowId = flowId,
        Type = FlowNodeType.Message,
        ConfigJson = $$"""{"Blocks":[{"Type":"text","Text":"{{text}}","MediaId":null}],"Buttons":[]}""",
        X = 0,
        Y = 0,
    };

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(respond(request));
        }
    }

    private sealed class PassthroughProtector : IChannelCredentialsProtector
    {
        public string Protect(string plainText) => plainText;
        public string Unprotect(string protectedText) => protectedText;
    }

    private sealed class NoOpNotificationService : INotificationService
    {
        public Task PushAsync(Guid userId, string type, object payload, CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>Ин тестҳо ягон action:delay надоранд — Schedule ҳеҷ гоҳ даъват намешавад.</summary>
    private sealed class NonFunctionalBackgroundJobClient : Hangfire.IBackgroundJobClient
    {
        public string Create(Hangfire.Common.Job job, Hangfire.States.IState state) => throw new NotSupportedException();
        public bool ChangeState(string jobId, Hangfire.States.IState state, string? expectedState) => throw new NotSupportedException();
    }

    private static (FlowTriggerProcessor Processor, FakeHttpMessageHandler Handler) MakeProcessor(AppDbContext db)
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"message_id":"mid.1"}""", Encoding.UTF8, "application/json"),
        });
        var httpClient = new HttpClient(handler);
        var provider = new InstagramProvider(
            httpClient, new PassthroughProtector(), new ConfigurationBuilder().Build(), db,
            new NoOpNotificationService(), new MemoryCache(new MemoryCacheOptions()),
            new InstagramFollowCheckRateLimiter(), NullLogger<InstagramProvider>.Instance);
        var engine = new FlowEngine(db, provider, new NonFunctionalBackgroundJobClient(), httpClient, NullLogger<FlowEngine>.Instance);
        return (new FlowTriggerProcessor(db, engine, NullLogger<FlowTriggerProcessor>.Instance), handler);
    }

    [Fact]
    public async Task ProcessCommentAsync_MatchingFlow_StartsSession()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        var flow = MakeFlow(channel.Id, "instagram_comment", "keyword", ["api"]);
        var node = MakeNode(flow.Id);
        db.Channels.Add(channel);
        db.Flows.Add(flow);
        db.FlowNodes.Add(node);
        await db.SaveChangesAsync();

        var (processor, handler) = MakeProcessor(db);
        var evt = new ParsedCommentEvent("comment-1", "actor-1", "someone", "api санҷиш", "media-1");

        await processor.ProcessCommentAsync(channel, evt, CancellationToken.None);

        var session = Assert.Single(await db.FlowSessions.ToListAsync());
        Assert.Equal(flow.Id, session.FlowId);
        Assert.Equal("comment-1", session.TriggerExternalId);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ProcessCommentAsync_SelfComment_IsIgnored()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        var flow = MakeFlow(channel.Id, "instagram_comment");
        db.Channels.Add(channel);
        db.Flows.Add(flow);
        db.FlowNodes.Add(MakeNode(flow.Id));
        await db.SaveChangesAsync();

        var (processor, _) = MakeProcessor(db);
        var evt = new ParsedCommentEvent("comment-1", channel.ExternalId, "self", "api", null);

        await processor.ProcessCommentAsync(channel, evt, CancellationToken.None);

        Assert.Empty(await db.FlowSessions.ToListAsync());
    }

    [Fact]
    public async Task ProcessCommentAsync_NoMatchingFlow_DoesNothing()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        var flow = MakeFlow(channel.Id, "instagram_comment", "keyword", ["api"]);
        db.Channels.Add(channel);
        db.Flows.Add(flow);
        db.FlowNodes.Add(MakeNode(flow.Id));
        await db.SaveChangesAsync();

        var (processor, _) = MakeProcessor(db);
        var evt = new ParsedCommentEvent("comment-1", "actor-1", "someone", "🔥👏", null);

        await processor.ProcessCommentAsync(channel, evt, CancellationToken.None);

        Assert.Empty(await db.FlowSessions.ToListAsync());
    }

    [Fact]
    public async Task ProcessCommentAsync_DuplicateCommentId_DoesNotStartASecondSession()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        var flow = MakeFlow(channel.Id, "instagram_comment");
        db.Channels.Add(channel);
        db.Flows.Add(flow);
        db.FlowNodes.Add(MakeNode(flow.Id));
        await db.SaveChangesAsync();

        var (processor, handler) = MakeProcessor(db);
        var evt = new ParsedCommentEvent("comment-1", "actor-1", "someone", "ҳар матн", null);

        await processor.ProcessCommentAsync(channel, evt, CancellationToken.None);
        await processor.ProcessCommentAsync(channel, evt, CancellationToken.None); // такрори webhook

        Assert.Single(await db.FlowSessions.ToListAsync());
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ProcessCommentAsync_InactiveFlow_IsIgnored()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        var flow = MakeFlow(channel.Id, "instagram_comment");
        flow.IsActive = false;
        db.Channels.Add(channel);
        db.Flows.Add(flow);
        db.FlowNodes.Add(MakeNode(flow.Id));
        await db.SaveChangesAsync();

        var (processor, _) = MakeProcessor(db);
        var evt = new ParsedCommentEvent("comment-1", "actor-1", "someone", "ҳар матн", null);

        await processor.ProcessCommentAsync(channel, evt, CancellationToken.None);

        Assert.Empty(await db.FlowSessions.ToListAsync());
    }

    [Fact]
    public async Task ProcessMessageAsync_MatchingDmFlow_StartsSession()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        var contact = new Conversation
        {
            Id = Guid.CreateVersion7(),
            ChannelId = channel.Id,
            ExternalId = "actor-1",
            Status = ConversationStatus.New,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var flow = MakeFlow(channel.Id, "instagram_dm");
        db.Channels.Add(channel);
        db.Conversations.Add(contact);
        db.Flows.Add(flow);
        db.FlowNodes.Add(MakeNode(flow.Id));
        await db.SaveChangesAsync();

        var (processor, _) = MakeProcessor(db);
        var message = new ParsedWebhookMessage(
            ConversationExternalId: "actor-1", ContactName: null, ContactAvatarUrl: null, MessageExternalId: "mid-1",
            Direction: MessageDirection.Inbound, Type: MessageType.Text, Body: "салом", MediaUrl: null, SentAt: DateTimeOffset.UtcNow);

        await processor.ProcessMessageAsync(channel, contact, message, CancellationToken.None);

        var session = Assert.Single(await db.FlowSessions.ToListAsync());
        Assert.Equal("mid-1", session.TriggerExternalId);
    }

    [Fact]
    public async Task ProcessMessageAsync_OutboundMessage_IsIgnored()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        var contact = new Conversation
        {
            Id = Guid.CreateVersion7(), ChannelId = channel.Id, ExternalId = "actor-1",
            Status = ConversationStatus.New, CreatedAt = DateTimeOffset.UtcNow,
        };
        var flow = MakeFlow(channel.Id, "instagram_dm");
        db.Channels.Add(channel);
        db.Conversations.Add(contact);
        db.Flows.Add(flow);
        db.FlowNodes.Add(MakeNode(flow.Id));
        await db.SaveChangesAsync();

        var (processor, _) = MakeProcessor(db);
        var message = new ParsedWebhookMessage(
            "actor-1", null, null, "mid-1", MessageDirection.Outbound, MessageType.Text, "салом", null, DateTimeOffset.UtcNow);

        await processor.ProcessMessageAsync(channel, contact, message, CancellationToken.None);

        Assert.Empty(await db.FlowSessions.ToListAsync());
    }

    [Fact]
    public async Task ProcessMessageAsync_WaitingSession_ResumesInsteadOfStartingNew()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        var contact = new Conversation
        {
            Id = Guid.CreateVersion7(), ChannelId = channel.Id, ExternalId = "actor-1",
            Status = ConversationStatus.New, CreatedAt = DateTimeOffset.UtcNow,
        };
        var flow = MakeFlow(channel.Id, "instagram_dm");
        var collectNode = new FlowNode
        {
            Id = Guid.CreateVersion7(), FlowId = flow.Id, Type = FlowNodeType.Action,
            ConfigJson = """{"Kind":"collect_input","VariableKey":"answer"}""",
        };
        db.Channels.Add(channel);
        db.Conversations.Add(contact);
        db.Flows.Add(flow);
        db.FlowNodes.Add(collectNode);
        db.FlowSessions.Add(new FlowSession
        {
            Id = Guid.CreateVersion7(),
            FlowId = flow.Id,
            ContactId = contact.Id,
            CurrentNodeId = collectNode.Id,
            Status = FlowSessionStatus.Waiting,
            WaitReason = FlowWaitReason.CollectInput,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var (processor, _) = MakeProcessor(db);
        var message = new ParsedWebhookMessage(
            "actor-1", null, null, "mid-2", MessageDirection.Inbound, MessageType.Text, "42", null, DateTimeOffset.UtcNow);

        await processor.ProcessMessageAsync(channel, contact, message, CancellationToken.None);

        // Сессияи мавҷуда resume шуд (на сессияи нав сохта шуд) — новобаста аз он ки flow дигар DM trigger дорад ё не.
        Assert.Single(await db.FlowSessions.ToListAsync());
        Assert.Equal("42", (await db.ContactVariables.SingleAsync()).Value);
    }
}
