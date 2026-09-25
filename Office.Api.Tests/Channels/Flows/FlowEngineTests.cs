using System.Net;
using System.Text;
using System.Text.Json;
using Hangfire.Common;
using Hangfire.States;
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

/// <summary>
/// FlowEngine бо DB воқеӣ (EF Core InMemory), HttpMessageHandler-и сохта (ҳамон алгуи
/// InstagramProviderFollowCheckTests) — ин синф ҳам DB, ҳам Graph API-ро истифода мебарад, пас
/// pure-тест-пазир нест.
/// </summary>
public class FlowEngineTests
{
    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Channel MakeChannel() => new()
    {
        Id = Guid.CreateVersion7(),
        Type = ChannelType.Instagram,
        Name = "Test IG channel",
        ExternalId = "17841400000000000",
        CredentialsEncrypted = """{"instagramAccountId":"17841400000000000","accessToken":"tok"}""",
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static Conversation MakeContact(Guid channelId, DateTimeOffset? windowExpiresAt = null) => new()
    {
        Id = Guid.CreateVersion7(),
        ChannelId = channelId,
        ExternalId = "1254001234567890",
        ContactName = "Фаридун Эшонов",
        ContactUsername = "eshonov.f1",
        Status = ConversationStatus.New,
        WindowExpiresAt = windowExpiresAt ?? DateTimeOffset.UtcNow.AddHours(1),
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static Flow MakeFlow(Guid channelId) => new()
    {
        Id = Guid.CreateVersion7(),
        ChannelId = channelId,
        Name = "Test flow",
        IsActive = true,
        TriggerType = "instagram_dm",
        TriggerConfigJson = """{"MatchMode":"all","Keywords":[],"PostScope":"all","PostIds":[]}""",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static FlowNode MakeNode(Guid flowId, FlowNodeType type, object config, double x = 0, double y = 0) => new()
    {
        Id = Guid.CreateVersion7(),
        FlowId = flowId,
        Type = type,
        ConfigJson = JsonSerializer.Serialize(config),
        X = x,
        Y = y,
    };

    private static FlowEdge MakeEdge(Guid flowId, Guid fromNodeId, string fromPort, Guid toNodeId) => new()
    {
        Id = Guid.CreateVersion7(),
        FlowId = flowId,
        FromNodeId = fromNodeId,
        FromPort = fromPort,
        ToNodeId = toNodeId,
    };

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> RequestUrls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUrls.Add(request.RequestUri!.ToString());
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

    private sealed class RecordingBackgroundJobClient : Hangfire.IBackgroundJobClient
    {
        public List<(string Method, TimeSpan? Delay)> Scheduled { get; } = [];

        public string Create(Job job, IState state)
        {
            var delay = state is ScheduledState scheduled ? scheduled.EnqueueAt - DateTime.UtcNow : (TimeSpan?)null;
            Scheduled.Add((job.Method.Name, delay));
            return Guid.NewGuid().ToString();
        }

        public bool ChangeState(string jobId, IState state, string? expectedState) => true;
    }

    private static (InstagramProvider Provider, FakeHttpMessageHandler Handler, RecordingBackgroundJobClient Jobs, FlowEngine Engine) MakeEngine(
        AppDbContext db, Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        var handler = new FakeHttpMessageHandler(respond ?? (_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"message_id":"mid.123"}""", Encoding.UTF8, "application/json"),
        }));
        var provider = new InstagramProvider(
            new HttpClient(handler), new PassthroughProtector(), new ConfigurationBuilder().Build(), db,
            new NoOpNotificationService(), new MemoryCache(new MemoryCacheOptions()),
            new InstagramFollowCheckRateLimiter(), NullLogger<InstagramProvider>.Instance);
        var jobs = new RecordingBackgroundJobClient();
        var engine = new FlowEngine(db, provider, jobs, new HttpClient(handler), NullLogger<FlowEngine>.Instance);
        return (provider, handler, jobs, engine);
    }

    [Fact]
    public async Task StartAsync_SimpleTextMessage_SendsAndFinishes()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        var contact = MakeContact(channel.Id);
        var flow = MakeFlow(channel.Id);
        var node = MakeNode(flow.Id, FlowNodeType.Message, new MessageNodeConfig([new MessageBlock(MessageBlock.TypeText, "Салом!", null)], []));
        db.Channels.Add(channel);
        db.Conversations.Add(contact);
        db.Flows.Add(flow);
        db.FlowNodes.Add(node);
        await db.SaveChangesAsync();

        var (_, handler, _, engine) = MakeEngine(db);
        await engine.StartAsync(flow, contact.Id, CancellationToken.None);

        var session = Assert.Single(await db.FlowSessions.ToListAsync());
        Assert.Equal(FlowSessionStatus.Finished, session.Status);
        Assert.Equal(1, session.StepCount);
        Assert.Single(handler.RequestUrls);
        Assert.Single(await db.FlowSessionSteps.ToListAsync());
    }

    [Fact]
    public async Task StartAsync_InterpolatesContactFields()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        var contact = MakeContact(channel.Id);
        var flow = MakeFlow(channel.Id);
        var node = MakeNode(flow.Id, FlowNodeType.Message, new MessageNodeConfig([new MessageBlock(MessageBlock.TypeText, "Салом, {{firstName}}!", null)], []));
        db.Channels.Add(channel);
        db.Conversations.Add(contact);
        db.Flows.Add(flow);
        db.FlowNodes.Add(node);
        await db.SaveChangesAsync();

        string? sentBody = null;
        var (_, _, _, engine) = MakeEngine(db, req =>
        {
            sentBody = req.Content!.ReadAsStringAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"message_id":"mid.1"}""") };
        });

        await engine.StartAsync(flow, contact.Id, CancellationToken.None);

        // JsonContent.Create экранизатсияи Юникодро истифода мебарад (\uXXXX барои кириллик) —
        // ин барои Meta бехатар аст (JSON-и дуруст, тасдиқшуда зинда дар Фазаи 10/11), вале маънои
        // онро дорад, ки санҷиш бояд JSON-ро decode кунад, на матни хомро мустақим ҷустуҷӯ кунад.
        using var doc = JsonDocument.Parse(sentBody!);
        var text = doc.RootElement.GetProperty("message").GetProperty("text").GetString();
        Assert.Equal("Салом, Фаридун!", text);
    }

    [Fact]
    public async Task StartAsync_MessageWithButtons_PausesWaitingForButtonClick()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        var contact = MakeContact(channel.Id);
        var flow = MakeFlow(channel.Id);
        var node = MakeNode(flow.Id, FlowNodeType.Message,
            new MessageNodeConfig([new MessageBlock(MessageBlock.TypeText, "Интихоб кунед", null)],
                [new MessageButton("Ҳа", MessageButton.ActionNext, null, false)]));
        db.Channels.Add(channel);
        db.Conversations.Add(contact);
        db.Flows.Add(flow);
        db.FlowNodes.Add(node);
        await db.SaveChangesAsync();

        var (_, _, _, engine) = MakeEngine(db);
        await engine.StartAsync(flow, contact.Id, CancellationToken.None);

        var session = Assert.Single(await db.FlowSessions.ToListAsync());
        Assert.Equal(FlowSessionStatus.Waiting, session.Status);
        Assert.Equal(FlowWaitReason.ButtonClick, session.WaitReason);
        Assert.Equal(node.Id, session.CurrentNodeId);
    }

    [Fact]
    public async Task ResumeFromButtonAsync_ValidClick_AdvancesToNextNode()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        var contact = MakeContact(channel.Id);
        var flow = MakeFlow(channel.Id);
        var buttonNode = MakeNode(flow.Id, FlowNodeType.Message,
            new MessageNodeConfig([], [new MessageButton("Ҳа", MessageButton.ActionNext, null, false)]));
        var nextNode = MakeNode(flow.Id, FlowNodeType.Message, new MessageNodeConfig([new MessageBlock(MessageBlock.TypeText, "Ташаккур!", null)], []));
        var edge = MakeEdge(flow.Id, buttonNode.Id, "button:0", nextNode.Id);
        db.Channels.Add(channel);
        db.Conversations.Add(contact);
        db.Flows.Add(flow);
        db.FlowNodes.AddRange(buttonNode, nextNode);
        db.FlowEdges.Add(edge);
        await db.SaveChangesAsync();

        var (_, handler, _, engine) = MakeEngine(db);
        await engine.StartAsync(flow, contact.Id, CancellationToken.None);
        var session = await db.FlowSessions.SingleAsync();

        await engine.ResumeFromButtonAsync($"{session.Id}:{buttonNode.Id}:0", CancellationToken.None);

        await db.Entry(session).ReloadAsync();
        Assert.Equal(FlowSessionStatus.Finished, session.Status);
        Assert.Equal(nextNode.Id, session.CurrentNodeId);
        Assert.Equal(2, handler.RequestUrls.Count); // паёми аввал (бо тугма) + паёми дуюм
    }

    [Fact]
    public async Task ResumeFromButtonAsync_StaleClickWithoutAllowRepeat_IsIgnored()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        var contact = MakeContact(channel.Id);
        var flow = MakeFlow(channel.Id);
        var buttonNode = MakeNode(flow.Id, FlowNodeType.Message,
            new MessageNodeConfig([], [new MessageButton("Ҳа", MessageButton.ActionNext, null, false)]));
        db.Channels.Add(channel);
        db.Conversations.Add(contact);
        db.Flows.Add(flow);
        db.FlowNodes.Add(buttonNode);
        await db.SaveChangesAsync();

        var (_, _, _, engine) = MakeEngine(db);
        await engine.StartAsync(flow, contact.Id, CancellationToken.None);
        var session = await db.FlowSessions.SingleAsync();
        session.Status = FlowSessionStatus.Finished; // "аллакай дур шудааст" — қадами кӯҳна
        session.WaitReason = null;
        await db.SaveChangesAsync();

        await engine.ResumeFromButtonAsync($"{session.Id}:{buttonNode.Id}:0", CancellationToken.None);

        await db.Entry(session).ReloadAsync();
        Assert.Equal(FlowSessionStatus.Finished, session.Status); // тағйир наёфт
    }

    [Fact]
    public async Task ResumeFromButtonAsync_StaleClickWithAllowRepeat_StillAdvances()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        var contact = MakeContact(channel.Id);
        var flow = MakeFlow(channel.Id);
        var buttonNode = MakeNode(flow.Id, FlowNodeType.Message,
            new MessageNodeConfig([], [new MessageButton("Менюи асосӣ", MessageButton.ActionNext, null, true)]));
        var nextNode = MakeNode(flow.Id, FlowNodeType.Message, new MessageNodeConfig([new MessageBlock(MessageBlock.TypeText, "Салом боз!", null)], []));
        var edge = MakeEdge(flow.Id, buttonNode.Id, "button:0", nextNode.Id);
        db.Channels.Add(channel);
        db.Conversations.Add(contact);
        db.Flows.Add(flow);
        db.FlowNodes.AddRange(buttonNode, nextNode);
        db.FlowEdges.Add(edge);
        await db.SaveChangesAsync();

        var (_, _, _, engine) = MakeEngine(db);
        await engine.StartAsync(flow, contact.Id, CancellationToken.None);
        var session = await db.FlowSessions.SingleAsync();
        session.Status = FlowSessionStatus.Finished;
        session.WaitReason = null;
        await db.SaveChangesAsync();

        await engine.ResumeFromButtonAsync($"{session.Id}:{buttonNode.Id}:0", CancellationToken.None);

        await db.Entry(session).ReloadAsync();
        Assert.Equal(FlowSessionStatus.Finished, session.Status);
        Assert.Equal(nextNode.Id, session.CurrentNodeId); // боз ҳам пеш рафт
    }

    [Fact]
    public async Task Condition_VariableMatch_TakesMatchBranch()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        var contact = MakeContact(channel.Id);
        db.ContactVariables.Add(new ContactVariable { ContactId = contact.Id, Key = "city", Value = "Душанбе" });
        var flow = MakeFlow(channel.Id);
        var conditionNode = MakeNode(flow.Id, FlowNodeType.Condition,
            new ConditionNodeConfig(ConditionNodeConfig.MatchAll, [new ConditionRule("city", ConditionEvaluator.OpEquals, "Душанбе")]));
        var matchNode = MakeNode(flow.Id, FlowNodeType.Message, new MessageNodeConfig([new MessageBlock(MessageBlock.TypeText, "Матч!", null)], []));
        var nomatchNode = MakeNode(flow.Id, FlowNodeType.Message, new MessageNodeConfig([new MessageBlock(MessageBlock.TypeText, "Не!", null)], []));
        db.Channels.Add(channel);
        db.Conversations.Add(contact);
        db.Flows.Add(flow);
        db.FlowNodes.AddRange(conditionNode, matchNode, nomatchNode);
        db.FlowEdges.AddRange(
            MakeEdge(flow.Id, conditionNode.Id, "match", matchNode.Id),
            MakeEdge(flow.Id, conditionNode.Id, "nomatch", nomatchNode.Id));
        await db.SaveChangesAsync();

        var (_, handler, _, engine) = MakeEngine(db);
        await engine.StartAsync(flow, contact.Id, CancellationToken.None);

        var session = await db.FlowSessions.SingleAsync();
        Assert.Equal(matchNode.Id, session.CurrentNodeId);
        Assert.Equal(FlowSessionStatus.Finished, session.Status);
    }

    [Fact]
    public async Task Action_AddTags_PersistsAndAdvances()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        var contact = MakeContact(channel.Id);
        var flow = MakeFlow(channel.Id);
        var node = MakeNode(flow.Id, FlowNodeType.Action, new ActionNodeConfig(ActionNodeConfig.KindAddTags, Tags: ["vip"]));
        db.Channels.Add(channel);
        db.Conversations.Add(contact);
        db.Flows.Add(flow);
        db.FlowNodes.Add(node);
        await db.SaveChangesAsync();

        var (_, _, _, engine) = MakeEngine(db);
        await engine.StartAsync(flow, contact.Id, CancellationToken.None);

        Assert.Equal("vip", (await db.ContactTags.SingleAsync()).Tag);
        Assert.Equal(FlowSessionStatus.Finished, (await db.FlowSessions.SingleAsync()).Status);
    }

    [Fact]
    public async Task Action_Delay_SchedulesHangfireJobAndWaits()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        var contact = MakeContact(channel.Id);
        var flow = MakeFlow(channel.Id);
        var node = MakeNode(flow.Id, FlowNodeType.Action, new ActionNodeConfig(ActionNodeConfig.KindDelay, DelayMinutes: 60));
        db.Channels.Add(channel);
        db.Conversations.Add(contact);
        db.Flows.Add(flow);
        db.FlowNodes.Add(node);
        await db.SaveChangesAsync();

        var (_, _, jobs, engine) = MakeEngine(db);
        await engine.StartAsync(flow, contact.Id, CancellationToken.None);

        var session = await db.FlowSessions.SingleAsync();
        Assert.Equal(FlowSessionStatus.Waiting, session.Status);
        Assert.Equal(FlowWaitReason.Delay, session.WaitReason);
        Assert.NotNull(session.ScheduledJobId);
        var scheduled = Assert.Single(jobs.Scheduled);
        Assert.Equal(nameof(FlowEngineJob.ResumeFromDelayAsync), scheduled.Method);
        Assert.True(scheduled.Delay is { TotalMinutes: > 55 and < 65 });
    }

    [Fact]
    public async Task ResumeFromDelayAsync_AfterWaiting_Advances()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        var contact = MakeContact(channel.Id);
        var flow = MakeFlow(channel.Id);
        var delayNode = MakeNode(flow.Id, FlowNodeType.Action, new ActionNodeConfig(ActionNodeConfig.KindDelay, DelayMinutes: 5));
        var nextNode = MakeNode(flow.Id, FlowNodeType.Message, new MessageNodeConfig([new MessageBlock(MessageBlock.TypeText, "Бедор шудам!", null)], []));
        db.Channels.Add(channel);
        db.Conversations.Add(contact);
        db.Flows.Add(flow);
        db.FlowNodes.AddRange(delayNode, nextNode);
        db.FlowEdges.Add(MakeEdge(flow.Id, delayNode.Id, "default", nextNode.Id));
        await db.SaveChangesAsync();

        var (_, handler, _, engine) = MakeEngine(db);
        await engine.StartAsync(flow, contact.Id, CancellationToken.None);
        var session = await db.FlowSessions.SingleAsync();

        await engine.ResumeFromDelayAsync(session.Id, CancellationToken.None);

        await db.Entry(session).ReloadAsync();
        Assert.Equal(FlowSessionStatus.Finished, session.Status);
        Assert.Equal(nextNode.Id, session.CurrentNodeId);
        Assert.Null(session.ScheduledJobId);
        Assert.Single(handler.RequestUrls); // паёми "Бедор шудам!" танҳо
    }

    [Fact]
    public async Task Action_CollectInput_WaitsThenStoresVariableOnResume()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        var contact = MakeContact(channel.Id);
        var flow = MakeFlow(channel.Id);
        var collectNode = MakeNode(flow.Id, FlowNodeType.Action, new ActionNodeConfig(ActionNodeConfig.KindCollectInput, VariableKey: "city"));
        var nextNode = MakeNode(flow.Id, FlowNodeType.Message, new MessageNodeConfig([new MessageBlock(MessageBlock.TypeText, "Гирифтам: {{city}}", null)], []));
        db.Channels.Add(channel);
        db.Conversations.Add(contact);
        db.Flows.Add(flow);
        db.FlowNodes.AddRange(collectNode, nextNode);
        db.FlowEdges.Add(MakeEdge(flow.Id, collectNode.Id, "default", nextNode.Id));
        await db.SaveChangesAsync();

        string? sentBody = null;
        var (_, _, _, engine) = MakeEngine(db, req =>
        {
            sentBody = req.Content?.ReadAsStringAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"message_id":"mid.1"}""") };
        });
        await engine.StartAsync(flow, contact.Id, CancellationToken.None);
        var session = await db.FlowSessions.SingleAsync();
        Assert.Equal(FlowSessionStatus.Waiting, session.Status);
        Assert.Equal(FlowWaitReason.CollectInput, session.WaitReason);

        await engine.ResumeFromMessageAsync(session.Id, "Душанбе", CancellationToken.None);

        await db.Entry(session).ReloadAsync();
        Assert.Equal(FlowSessionStatus.Finished, session.Status);
        using var doc = JsonDocument.Parse(sentBody!);
        Assert.Equal("Гирифтам: Душанбе", doc.RootElement.GetProperty("message").GetProperty("text").GetString());
        Assert.Equal("Душанбе", (await db.ContactVariables.SingleAsync()).Value);
    }

    [Fact]
    public async Task Message_WindowClosed_WaitsThenRetriesOnNextMessage()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        var contact = MakeContact(channel.Id, windowExpiresAt: DateTimeOffset.UtcNow.AddHours(-1)); // тирезаи баста
        var flow = MakeFlow(channel.Id);
        var node = MakeNode(flow.Id, FlowNodeType.Message, new MessageNodeConfig([new MessageBlock(MessageBlock.TypeText, "Салом!", null)], []));
        db.Channels.Add(channel);
        db.Conversations.Add(contact);
        db.Flows.Add(flow);
        db.FlowNodes.Add(node);
        await db.SaveChangesAsync();

        var (_, handler, _, engine) = MakeEngine(db);
        await engine.StartAsync(flow, contact.Id, CancellationToken.None);

        var session = await db.FlowSessions.SingleAsync();
        Assert.Equal(FlowSessionStatus.Waiting, session.Status);
        Assert.Equal(FlowWaitReason.WindowClosed, session.WaitReason);
        Assert.Empty(handler.RequestUrls); // ҳеҷ дархост нарафт — тиреза баста буд

        // Паёми нав тирезаро мекушояд — ниг. ConversationWindowCalculator (берун аз доираи ин тест).
        contact.WindowExpiresAt = DateTimeOffset.UtcNow.AddHours(1);
        await db.SaveChangesAsync();

        await engine.ResumeFromMessageAsync(session.Id, "ҳар матн", CancellationToken.None);

        await db.Entry(session).ReloadAsync();
        Assert.Equal(FlowSessionStatus.Finished, session.Status);
        Assert.Single(handler.RequestUrls);
    }

    [Fact]
    public async Task Message_CommentTriggeredContactWithNoWindow_UsesPrivateReplyByCommentId()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        // Контакти нав аз коментарий: ҳеҷ гоҳ DM нафиристодааст — WindowExpiresAt=null (на
        // "баста", балки "ҳеҷ гоҳ кушода нашуда"). MakeContact(channelId, null) намесозад чунин
        // ҳолатро (null аргумент ба +1соат мубаддал мешавад), пас Conversation мустақим сохта мешавад.
        var contact = new Conversation
        {
            Id = Guid.CreateVersion7(),
            ChannelId = channel.Id,
            ExternalId = "1254001234567890",
            ContactUsername = "eshonov.f1",
            Status = ConversationStatus.New,
            WindowExpiresAt = null,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var flow = MakeFlow(channel.Id);
        flow.TriggerType = "instagram_comment";
        var node = MakeNode(flow.Id, FlowNodeType.Message, new MessageNodeConfig([new MessageBlock(MessageBlock.TypeText, "Салом!", null)], []));
        db.Channels.Add(channel);
        db.Conversations.Add(contact);
        db.Flows.Add(flow);
        db.FlowNodes.Add(node);
        await db.SaveChangesAsync();

        string? sentBody = null;
        var (_, handler, _, engine) = MakeEngine(db, req =>
        {
            sentBody = req.Content!.ReadAsStringAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"message_id":"mid.1"}""") };
        });

        await engine.StartAsync(flow, contact.Id, CancellationToken.None, triggerExternalId: "comment_777");

        var session = await db.FlowSessions.SingleAsync();
        // На Waiting/WindowClosed (тарзи кӯҳна барои WindowExpiresAt=null ҳамеша "кушода" мешумурд,
        // вале SendMessageAsync-и муқаррарӣ аз ҷониби Meta рад мешуд) — паём воқеан фиристода шуд.
        Assert.Equal(FlowSessionStatus.Finished, session.Status);
        Assert.Single(handler.RequestUrls);

        using var doc = JsonDocument.Parse(sentBody!);
        Assert.Equal("comment_777", doc.RootElement.GetProperty("recipient").GetProperty("comment_id").GetString());
        Assert.Equal("Салом!", doc.RootElement.GetProperty("message").GetProperty("text").GetString());
    }

    [Fact]
    public async Task Message_WithMediaAndCaption_SendsAttachmentThenCaptionText()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        var contact = MakeContact(channel.Id); // тирезаи кушода — SendMediaMessageAsync-и муқаррарӣ
        var flow = MakeFlow(channel.Id);
        var node = MakeNode(flow.Id, FlowNodeType.Message,
            new MessageNodeConfig(
                [new MessageBlock(MessageBlock.TypeText, "Ана расми шумо!", null), new MessageBlock(MessageBlock.TypeImage, null, "attach_123")],
                []));
        db.Channels.Add(channel);
        db.Conversations.Add(contact);
        db.Flows.Add(flow);
        db.FlowNodes.Add(node);
        await db.SaveChangesAsync();

        var bodies = new List<string>();
        var (_, handler, _, engine) = MakeEngine(db, req =>
        {
            bodies.Add(req.Content!.ReadAsStringAsync().Result);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"message_id":"mid.1"}""") };
        });

        await engine.StartAsync(flow, contact.Id, CancellationToken.None);

        Assert.Equal(FlowSessionStatus.Finished, (await db.FlowSessions.SingleAsync()).Status);
        Assert.Equal(2, handler.RequestUrls.Count); // attachment якум, баъд caption (Send API як object=як дархост)

        using var attachmentDoc = JsonDocument.Parse(bodies[0]);
        Assert.Equal("image", attachmentDoc.RootElement.GetProperty("message").GetProperty("attachment").GetProperty("type").GetString());
        Assert.Equal("attach_123", attachmentDoc.RootElement.GetProperty("message").GetProperty("attachment").GetProperty("payload").GetProperty("attachment_id").GetString());

        using var captionDoc = JsonDocument.Parse(bodies[1]);
        Assert.Equal("Ана расми шумо!", captionDoc.RootElement.GetProperty("message").GetProperty("text").GetString());
    }

    /// <summary>
    /// Регрессия: пештар нод бо media+тугма якҷоя (масалан "Лид-магнит"-и оғозин) хомӯшона
    /// media-ро партофта, танҳо матни тугмадорро мефиристод — Send API-и Meta як message object
    /// мегирад (attachment ё button-template, на ҳарду якҷоя), пас ҳал ин аст: ду дархости
    /// пайдарпай (аввал attachment, баъд паёми тугмадор), на партофтани яке аз онҳо.
    /// </summary>
    [Fact]
    public async Task Message_WithMediaAndButtons_SendsAttachmentThenButtonMessage()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        var contact = MakeContact(channel.Id); // тирезаи кушода
        var flow = MakeFlow(channel.Id);
        var node = MakeNode(flow.Id, FlowNodeType.Message,
            new MessageNodeConfig(
                [new MessageBlock(MessageBlock.TypeText, "Мехоҳед видеогайдро бинед?", null), new MessageBlock(MessageBlock.TypeImage, null, "attach_lead")],
                [new MessageButton("Ҳа, мехоҳам!", MessageButton.ActionNext, null, false)]));
        db.Channels.Add(channel);
        db.Conversations.Add(contact);
        db.Flows.Add(flow);
        db.FlowNodes.Add(node);
        await db.SaveChangesAsync();

        var bodies = new List<string>();
        var (_, handler, _, engine) = MakeEngine(db, req =>
        {
            bodies.Add(req.Content!.ReadAsStringAsync().Result);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"message_id":"mid.1"}""") };
        });

        await engine.StartAsync(flow, contact.Id, CancellationToken.None);

        var session = await db.FlowSessions.SingleAsync();
        Assert.Equal(FlowSessionStatus.Waiting, session.Status);
        Assert.Equal(FlowWaitReason.ButtonClick, session.WaitReason);
        Assert.Equal(2, handler.RequestUrls.Count); // расм якум, баъд паёми тугмадор — на яке ба ҷои дигаре

        using var attachmentDoc = JsonDocument.Parse(bodies[0]);
        var attachment = attachmentDoc.RootElement.GetProperty("message").GetProperty("attachment");
        Assert.Equal("image", attachment.GetProperty("type").GetString());
        Assert.Equal("attach_lead", attachment.GetProperty("payload").GetProperty("attachment_id").GetString());

        using var buttonDoc = JsonDocument.Parse(bodies[1]);
        var buttonPayload = buttonDoc.RootElement.GetProperty("message").GetProperty("attachment").GetProperty("payload");
        Assert.Equal("Мехоҳед видеогайдро бинед?", buttonPayload.GetProperty("text").GetString());
        Assert.Equal("Ҳа, мехоҳам!", buttonPayload.GetProperty("buttons")[0].GetProperty("title").GetString());
    }

    /// <summary>
    /// Регрессия барои хатои воқеии истеҳсол (2026-09-17, "Лид-магнит"-и оғозин: расм+тугмаи
    /// "next"): пештар шарти Private Reply тугмаро истисно мекард (танҳо url кор мекард), пас
    /// коментатори аввалин (WindowExpiresAt=null) ба рафтори кӯҳна мегузашт — SendButtonMessageAsync
    /// (recipient.id) ба контакти бе тиреза, ки Meta бояд рад мекард ("ба DM ҳеҷ чиз намеояд").
    /// Ҳозир бояд тавассути Private Reply бо тугмаи postback гузарад ва Waiting/ButtonClick монад.
    /// </summary>
    [Fact]
    public async Task Message_CommentTriggeredNoWindowWithMediaAndNextButton_UsesPrivateReplyWithPostbackButton()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        var contact = new Conversation
        {
            Id = Guid.CreateVersion7(),
            ChannelId = channel.Id,
            ExternalId = "1254001234567890",
            ContactUsername = "eshonov.f1",
            Status = ConversationStatus.New,
            WindowExpiresAt = null, // ҳеҷ гоҳ DM нафиристодааст — танҳо коментарий (айнан ҳолати воқеӣ)
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var flow = MakeFlow(channel.Id);
        flow.TriggerType = "instagram_comment";
        var node = MakeNode(flow.Id, FlowNodeType.Message,
            new MessageNodeConfig(
                [new MessageBlock(MessageBlock.TypeText, "Мехоҳед видеогайдро бинед?", null), new MessageBlock(MessageBlock.TypeImage, null, "attach_lead")],
                [new MessageButton("Ҳа, мехоҳам!", MessageButton.ActionNext, null, false)]));
        db.Channels.Add(channel);
        db.Conversations.Add(contact);
        db.Flows.Add(flow);
        db.FlowNodes.Add(node);
        await db.SaveChangesAsync();

        string? sentBody = null;
        var (_, handler, _, engine) = MakeEngine(db, req =>
        {
            sentBody = req.Content!.ReadAsStringAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"message_id":"mid.1"}""") };
        });

        await engine.StartAsync(flow, contact.Id, CancellationToken.None, triggerExternalId: "comment_888");

        var session = await db.FlowSessions.SingleAsync();
        Assert.Equal(FlowSessionStatus.Waiting, session.Status);
        Assert.Equal(FlowWaitReason.ButtonClick, session.WaitReason);
        Assert.Single(handler.RequestUrls); // Private Reply — як дархост, на ду (media партофта шуд, на фиристода)

        using var doc = JsonDocument.Parse(sentBody!);
        Assert.Equal("comment_888", doc.RootElement.GetProperty("recipient").GetProperty("comment_id").GetString());
        var buttonPayload = doc.RootElement.GetProperty("message").GetProperty("attachment").GetProperty("payload");
        Assert.Equal("Мехоҳед видеогайдро бинед?", buttonPayload.GetProperty("text").GetString());
        var button = buttonPayload.GetProperty("buttons")[0];
        Assert.Equal("postback", button.GetProperty("type").GetString());
        Assert.Equal("Ҳа, мехоҳам!", button.GetProperty("title").GetString());
        Assert.Equal($"{session.Id}:{node.Id}:0", button.GetProperty("payload").GetString());
    }

    [Fact]
    public async Task Message_CommentTriggeredNoWindowWithMedia_UsesPrivateReplyMedia()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        var contact = new Conversation
        {
            Id = Guid.CreateVersion7(),
            ChannelId = channel.Id,
            ExternalId = "1254001234567890",
            ContactUsername = "eshonov.f1",
            Status = ConversationStatus.New,
            WindowExpiresAt = null, // ҳеҷ гоҳ DM нафиристодааст — танҳо коментарий
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var flow = MakeFlow(channel.Id);
        flow.TriggerType = "instagram_comment";
        var node = MakeNode(flow.Id, FlowNodeType.Message,
            new MessageNodeConfig([new MessageBlock(MessageBlock.TypeVideo, null, "attach_456")], []));
        db.Channels.Add(channel);
        db.Conversations.Add(contact);
        db.Flows.Add(flow);
        db.FlowNodes.Add(node);
        await db.SaveChangesAsync();

        string? sentBody = null;
        var (_, handler, _, engine) = MakeEngine(db, req =>
        {
            sentBody = req.Content!.ReadAsStringAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"message_id":"mid.1"}""") };
        });

        await engine.StartAsync(flow, contact.Id, CancellationToken.None, triggerExternalId: "comment_999");

        Assert.Equal(FlowSessionStatus.Finished, (await db.FlowSessions.SingleAsync()).Status);
        Assert.Single(handler.RequestUrls);

        using var doc = JsonDocument.Parse(sentBody!);
        Assert.Equal("comment_999", doc.RootElement.GetProperty("recipient").GetProperty("comment_id").GetString());
        Assert.Equal("video", doc.RootElement.GetProperty("message").GetProperty("attachment").GetProperty("type").GetString());
        Assert.Equal("attach_456", doc.RootElement.GetProperty("message").GetProperty("attachment").GetProperty("payload").GetProperty("attachment_id").GetString());
    }

    [Fact]
    public async Task LoopGuard_SelfReferencingGraph_FailsAfterFiftySteps()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        var contact = MakeContact(channel.Id);
        var flow = MakeFlow(channel.Id);
        // "Ноди аввал" бо ин ки ягон edge ба он ишора намекунад муайян мешавад — пас ҳалқаи
        // худ-ба-худ имконнопазир аст барои санҷиш (ин ном ба ин нод ишора мекунад ҳам). Ба ҷои
        // он, ду нод якдигарро давр мезананд, ва ноди аввал ба ин давр ворид мешавад.
        var startNode = MakeNode(flow.Id, FlowNodeType.Action, new ActionNodeConfig(ActionNodeConfig.KindAddTags, Tags: ["start"]));
        var loopNodeA = MakeNode(flow.Id, FlowNodeType.Action, new ActionNodeConfig(ActionNodeConfig.KindAddTags, Tags: ["a"]));
        var loopNodeB = MakeNode(flow.Id, FlowNodeType.Action, new ActionNodeConfig(ActionNodeConfig.KindAddTags, Tags: ["b"]));
        db.Channels.Add(channel);
        db.Conversations.Add(contact);
        db.Flows.Add(flow);
        db.FlowNodes.AddRange(startNode, loopNodeA, loopNodeB);
        db.FlowEdges.AddRange(
            MakeEdge(flow.Id, startNode.Id, "default", loopNodeA.Id),
            MakeEdge(flow.Id, loopNodeA.Id, "default", loopNodeB.Id),
            MakeEdge(flow.Id, loopNodeB.Id, "default", loopNodeA.Id));
        await db.SaveChangesAsync();

        var (_, _, _, engine) = MakeEngine(db);
        await engine.StartAsync(flow, contact.Id, CancellationToken.None);

        var session = await db.FlowSessions.SingleAsync();
        Assert.Equal(FlowSessionStatus.Failed, session.Status);
        Assert.Equal(FlowSessionLoopGuard.MaxSteps, session.StepCount);
        Assert.Contains(FlowSessionLoopGuard.MaxSteps.ToString(), session.Error);
    }

    [Fact]
    public async Task Action_GotoFlow_FinishesCurrentSessionAndStartsNewOne()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        var contact = MakeContact(channel.Id);
        var targetFlow = MakeFlow(channel.Id);
        var targetNode = MakeNode(targetFlow.Id, FlowNodeType.Message, new MessageNodeConfig([new MessageBlock(MessageBlock.TypeText, "Дар flow-и дигар!", null)], []));
        var sourceFlow = MakeFlow(channel.Id);
        var gotoNode = MakeNode(sourceFlow.Id, FlowNodeType.Action, new ActionNodeConfig(ActionNodeConfig.KindGotoFlow, TargetFlowId: targetFlow.Id));
        db.Channels.Add(channel);
        db.Conversations.Add(contact);
        db.Flows.AddRange(targetFlow, sourceFlow);
        db.FlowNodes.AddRange(targetNode, gotoNode);
        await db.SaveChangesAsync();

        var (_, handler, _, engine) = MakeEngine(db);
        await engine.StartAsync(sourceFlow, contact.Id, CancellationToken.None);

        var sessions = await db.FlowSessions.ToListAsync();
        Assert.Equal(2, sessions.Count);
        Assert.Single(sessions, s => s.FlowId == sourceFlow.Id && s.Status == FlowSessionStatus.Finished);
        Assert.Single(sessions, s => s.FlowId == targetFlow.Id && s.Status == FlowSessionStatus.Finished);
        Assert.Single(handler.RequestUrls);
    }

    [Theory]
    [InlineData(true, false, 0)]   // trial running        → runs
    [InlineData(false, false, 1)]  // trial over, no plan  → stopped
    [InlineData(true, true, 1)]    // channel disconnected → stopped
    public async Task MizojChannel_RunsOnlyWithAccessAndWhileConnected(bool trialRunning, bool disconnected, int blocked)
    {
        await using var db = CreateDb();
        var customer = new Customer
        {
            Id = Guid.NewGuid(), Email = "m@example.com", FullName = "M",
            TrialEndsAt = DateTimeOffset.UtcNow.AddDays(trialRunning ? 3 : -1),
        };
        var channel = MakeChannel();
        channel.CustomerId = customer.Id;
        channel.IsActive = !disconnected;
        var contact = MakeContact(channel.Id);
        var flow = MakeFlow(channel.Id);
        var node = MakeNode(flow.Id, FlowNodeType.Message, new MessageNodeConfig([new MessageBlock(MessageBlock.TypeText, "Салом!", null)], []));
        db.Customers.Add(customer);
        db.Channels.Add(channel);
        db.Conversations.Add(contact);
        db.Flows.Add(flow);
        db.FlowNodes.Add(node);
        await db.SaveChangesAsync();

        var (_, handler, _, engine) = MakeEngine(db);
        await engine.StartAsync(flow, contact.Id, CancellationToken.None);

        var session = await db.FlowSessions.SingleAsync();
        if (blocked == 1)
        {
            Assert.Equal(FlowSessionStatus.Failed, session.Status);
            Assert.Contains("тариф", session.Error);
            Assert.Empty(handler.RequestUrls); // nothing sent on the мизоҷ's behalf
        }
        else
        {
            Assert.Equal(FlowSessionStatus.Finished, session.Status);
            Assert.Single(handler.RequestUrls);
        }
    }

    [Fact]
    public async Task WaitingSession_DoesNotResumeAfterThePlanRanOut()
    {
        // A delay scheduled during the trial fires after it ended: the rest of the flow must not run.
        await using var db = CreateDb();
        var customer = new Customer { Id = Guid.NewGuid(), Email = "m@example.com", FullName = "M", TrialEndsAt = DateTimeOffset.UtcNow.AddDays(1) };
        var channel = MakeChannel();
        channel.CustomerId = customer.Id;
        var contact = MakeContact(channel.Id);
        var flow = MakeFlow(channel.Id);
        var delay = MakeNode(flow.Id, FlowNodeType.Action, new ActionNodeConfig(ActionNodeConfig.KindDelay, DelayMinutes: 60));
        var after = MakeNode(flow.Id, FlowNodeType.Message, new MessageNodeConfig([new MessageBlock(MessageBlock.TypeText, "Баъд аз таъхир", null)], []));
        db.Customers.Add(customer);
        db.Channels.Add(channel);
        db.Conversations.Add(contact);
        db.Flows.Add(flow);
        db.FlowNodes.AddRange(delay, after);
        db.FlowEdges.Add(MakeEdge(flow.Id, delay.Id, "default", after.Id));
        await db.SaveChangesAsync();

        var (_, handler, _, engine) = MakeEngine(db);
        await engine.StartAsync(flow, contact.Id, CancellationToken.None);
        var session = await db.FlowSessions.SingleAsync();
        Assert.Equal(FlowSessionStatus.Waiting, session.Status);

        customer.TrialEndsAt = DateTimeOffset.UtcNow.AddMinutes(-1); // the trial ends while it waits
        await db.SaveChangesAsync();
        await engine.ResumeFromDelayAsync(session.Id, CancellationToken.None);

        Assert.Equal(FlowSessionStatus.Failed, (await db.FlowSessions.SingleAsync()).Status);
        Assert.Empty(handler.RequestUrls);
    }

    [Fact]
    public async Task Action_GotoFlow_NeverStartsAFlowOnAnotherChannel()
    {
        // Another owner's flow (another мизоҷ, or the company) must not run for this contact,
        // even with its exact id — the engine has no tenant filter to stop it otherwise.
        await using var db = CreateDb();
        var ownChannel = MakeChannel();
        var otherChannel = MakeChannel();
        otherChannel.ExternalId = "other-account";
        var contact = MakeContact(ownChannel.Id);
        var foreignFlow = MakeFlow(otherChannel.Id);
        var foreignNode = MakeNode(foreignFlow.Id, FlowNodeType.Message, new MessageNodeConfig([new MessageBlock(MessageBlock.TypeText, "Паёми бегона", null)], []));
        var sourceFlow = MakeFlow(ownChannel.Id);
        var gotoNode = MakeNode(sourceFlow.Id, FlowNodeType.Action, new ActionNodeConfig(ActionNodeConfig.KindGotoFlow, TargetFlowId: foreignFlow.Id));
        db.Channels.AddRange(ownChannel, otherChannel);
        db.Conversations.Add(contact);
        db.Flows.AddRange(foreignFlow, sourceFlow);
        db.FlowNodes.AddRange(foreignNode, gotoNode);
        await db.SaveChangesAsync();

        var (_, handler, _, engine) = MakeEngine(db);
        await engine.StartAsync(sourceFlow, contact.Id, CancellationToken.None);

        Assert.DoesNotContain(await db.FlowSessions.ToListAsync(), s => s.FlowId == foreignFlow.Id);
        Assert.Empty(handler.RequestUrls); // nothing was sent
    }

    /// <summary>
    /// Регрессия ҷиддӣ: пеш аз ин ислоҳ, ду flow ки ба ҳам goto_flow доранд (A→B→A→...)
    /// StackOverflowException месохтанд — реcursия бе марз тавассути StartAsync, ки процесси
    /// .NET-ро abadan мекушт (на хатои қобили catch, на танҳо як сессияи Failed). Ҳоло бояд
    /// бехатар қатъ шавад: сессияи аввал Finished (ба B "супоридааст"), сессияи дуюм Failed бо
    /// сабаби возеҳи ҳалқа — бе StackOverflow, бе сессияи сеюм/чорум.
    /// </summary>
    [Fact]
    public async Task Action_GotoFlow_MutualCycle_FailsSecondSessionInsteadOfCrashing()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        var contact = MakeContact(channel.Id);
        var flowA = MakeFlow(channel.Id);
        var flowB = MakeFlow(channel.Id);
        var gotoB = MakeNode(flowA.Id, FlowNodeType.Action, new ActionNodeConfig(ActionNodeConfig.KindGotoFlow, TargetFlowId: flowB.Id));
        var gotoA = MakeNode(flowB.Id, FlowNodeType.Action, new ActionNodeConfig(ActionNodeConfig.KindGotoFlow, TargetFlowId: flowA.Id));
        db.Channels.Add(channel);
        db.Conversations.Add(contact);
        db.Flows.AddRange(flowA, flowB);
        db.FlowNodes.AddRange(gotoB, gotoA);
        await db.SaveChangesAsync();

        var (_, _, _, engine) = MakeEngine(db);
        await engine.StartAsync(flowA, contact.Id, CancellationToken.None);

        var sessions = await db.FlowSessions.ToListAsync();
        Assert.Equal(2, sessions.Count); // на 3+ — занҷир дар ҳамин ҷо қатъ шуд, на такрор
        Assert.Single(sessions, s => s.FlowId == flowA.Id && s.Status == FlowSessionStatus.Finished);
        var failed = Assert.Single(sessions, s => s.FlowId == flowB.Id);
        Assert.Equal(FlowSessionStatus.Failed, failed.Status);
        Assert.Contains("ҳалқа", failed.Error);
    }

    [Fact]
    public async Task Action_HttpRequestToDisallowedUrl_SkipsButStillAdvances()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        var contact = MakeContact(channel.Id);
        var flow = MakeFlow(channel.Id);
        var node = MakeNode(flow.Id, FlowNodeType.Action, new ActionNodeConfig(ActionNodeConfig.KindHttpRequest, HttpUrl: "https://localhost/hook"));
        db.Channels.Add(channel);
        db.Conversations.Add(contact);
        db.Flows.Add(flow);
        db.FlowNodes.Add(node);
        await db.SaveChangesAsync();

        var (_, _, _, engine) = MakeEngine(db);
        await engine.StartAsync(flow, contact.Id, CancellationToken.None);

        Assert.Equal(FlowSessionStatus.Finished, (await db.FlowSessions.SingleAsync()).Status);
    }

    [Fact]
    public async Task StartAsync_EmptyGraph_DoesNotCreateSession()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        var contact = MakeContact(channel.Id);
        var flow = MakeFlow(channel.Id);
        db.Channels.Add(channel);
        db.Conversations.Add(contact);
        db.Flows.Add(flow);
        await db.SaveChangesAsync();

        var (_, _, _, engine) = MakeEngine(db);
        await engine.StartAsync(flow, contact.Id, CancellationToken.None);

        Assert.Empty(await db.FlowSessions.ToListAsync());
    }
}
