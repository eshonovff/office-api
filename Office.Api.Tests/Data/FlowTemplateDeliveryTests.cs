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
using Office.Api.Channels.Automation;
using Office.Api.Channels.Flows;
using Office.Api.Channels.Instagram;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Realtime;

namespace Office.Api.Tests.Data;

/// <summary>
/// Санҷиши вазифавӣ (на pure): шаблонҳои воқеии FlowTemplateSeeder ("Ҷавоб ба комментарий + DM"
/// ва "Ҷамъоварии контакт") ба Flow-и воқеӣ табдил дода мешаванд (айнан FlowTemplatesEndpoints,
/// тавассути FlowTemplateInstantiator-и муштарак), баъд FlowTriggerProcessor.ProcessCommentAsync/
/// ProcessMessageAsync-ро воқеан даъват мекунад — то ҷавоб диҳад: агар корбар дар DM ё дар
/// коментарий нависад, паём ВОҚЕАН ба Meta мерасад ё не (дархости HTTP-и воқеӣ, бо fake handler).
/// </summary>
public class FlowTemplateDeliveryTests
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

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(request.Content?.ReadAsStringAsync(cancellationToken).Result ?? "");
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
        public string Create(Job job, IState state) => Guid.NewGuid().ToString();
        public bool ChangeState(string jobId, IState state, string? expectedState) => true;
    }

    private static (FakeHttpMessageHandler Handler, FlowTriggerProcessor Trigger) MakeTrigger(AppDbContext db)
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"message_id":"mid.123"}""", Encoding.UTF8, "application/json"),
        });
        var provider = new InstagramProvider(
            new HttpClient(handler), new PassthroughProtector(), new ConfigurationBuilder().Build(), db,
            new NoOpNotificationService(), new MemoryCache(new MemoryCacheOptions()),
            new InstagramFollowCheckRateLimiter(), NullLogger<InstagramProvider>.Instance);
        var engine = new FlowEngine(db, provider, new RecordingBackgroundJobClient(), new HttpClient(handler), NullLogger<FlowEngine>.Instance);
        var trigger = new FlowTriggerProcessor(db, engine, NullLogger<FlowTriggerProcessor>.Instance);
        return (handler, trigger);
    }

    /// <summary>Айнан FlowTemplatesEndpoints.InstantiateAsync: шаблони воқеӣ (аз рӯи ном) → Flow+FlowNode+FlowEdge-и воқеӣ.</summary>
    private static async Task<Flow> InstantiateTemplateAsync(AppDbContext db, Guid channelId, string templateName, string triggerType, CancellationToken ct)
    {
        await FlowTemplateSeeder.SeedAsync(db, ct);
        var template = await db.FlowTemplates.SingleAsync(t => t.Name == templateName, ct);
        var definition = JsonSerializer.Deserialize<FlowTemplateDefinition>(template.DefinitionJson)!;

        var flow = new Flow
        {
            Id = Guid.CreateVersion7(),
            ChannelId = channelId,
            Name = templateName,
            IsActive = true,
            TriggerType = triggerType,
            TriggerConfigJson = JsonSerializer.Serialize(new AutomationTriggerConfig("all", [], "all", [])),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Flows.Add(flow);

        var (nodes, edges) = FlowTemplateInstantiator.Instantiate(definition, flow.Id);
        db.FlowNodes.AddRange(nodes);
        db.FlowEdges.AddRange(edges);
        await db.SaveChangesAsync(ct);
        return flow;
    }

    [Fact]
    public async Task CommentReplyTemplate_TriggeredByCommentFromFirstTimeCommenter_DeliversDmViaPrivateReply()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        db.Channels.Add(channel);
        await db.SaveChangesAsync();

        await InstantiateTemplateAsync(db, channel.Id, "Ҷавоб ба комментарий + DM", "instagram_comment", CancellationToken.None);
        var (handler, trigger) = MakeTrigger(db);

        var evt = new ParsedCommentEvent("comment-1", "1254001234567890", "eshonov.f1", "нарх?", "media-1");
        await trigger.ProcessCommentAsync(channel, evt, CancellationToken.None);

        var session = await db.FlowSessions.SingleAsync();
        Assert.Equal(FlowSessionStatus.Finished, session.Status);
        Assert.Single(handler.Bodies);

        using var doc = JsonDocument.Parse(handler.Bodies[0]);
        // Контакти нав ҳеҷ гоҳ DM нафиристодааст — тиреза нест, пас бояд тавассути
        // recipient.comment_id (Private Reply) равад, на recipient.id-и муқаррарӣ.
        Assert.Equal("comment-1", doc.RootElement.GetProperty("recipient").GetProperty("comment_id").GetString());
        Assert.Contains("Ташаккур", doc.RootElement.GetProperty("message").GetProperty("text").GetString());
    }

    [Fact]
    public async Task CommentReplyTemplate_TriggeredByFreshDm_DeliversViaNormalSend()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        db.Channels.Add(channel);
        await db.SaveChangesAsync();

        await InstantiateTemplateAsync(db, channel.Id, "Ҷавоб ба комментарий + DM", "instagram_dm", CancellationToken.None);
        var (handler, trigger) = MakeTrigger(db);

        // WebhookProcessor воқеан пеш аз FlowTriggerProcessor тирезаро мекушояд (паёми воридотӣ) —
        // ин ҷо ҳамон ҳолат тақлид карда мешавад.
        var contact = new Conversation
        {
            Id = Guid.CreateVersion7(),
            ChannelId = channel.Id,
            ExternalId = "1254001234567890",
            ContactUsername = "eshonov.f1",
            Status = ConversationStatus.New,
            WindowExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Conversations.Add(contact);
        await db.SaveChangesAsync();

        var message = new ParsedWebhookMessage(
            contact.ExternalId, contact.ContactUsername, null, "msg-1", MessageDirection.Inbound,
            MessageType.Text, "Нарх чанд?", null, DateTimeOffset.UtcNow);
        await trigger.ProcessMessageAsync(channel, contact, message, CancellationToken.None);

        var session = await db.FlowSessions.SingleAsync();
        Assert.Equal(FlowSessionStatus.Finished, session.Status);
        Assert.Single(handler.Bodies);

        using var doc = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal(contact.ExternalId, doc.RootElement.GetProperty("recipient").GetProperty("id").GetString());
        Assert.Contains("Ташаккур", doc.RootElement.GetProperty("message").GetProperty("text").GetString());
    }

    [Fact]
    public async Task ContactCollectionTemplate_TriggeredByComment_CompletesFullConversationAcrossWindowTransition()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        db.Channels.Add(channel);
        await db.SaveChangesAsync();

        await InstantiateTemplateAsync(db, channel.Id, "Ҷамъоварии контакт", "instagram_comment", CancellationToken.None);
        var (handler, trigger) = MakeTrigger(db);

        // Қадами 1: коммент аз контакти нав — "Салом! Номи шумо чист?" бояд тавассути Private
        // Reply равад (ҳеҷ тиреза нест) ва сессия дар CollectInput waiting монад.
        var evt = new ParsedCommentEvent("comment-1", "1254001234567890", "eshonov.f1", "нарх?", "media-1");
        await trigger.ProcessCommentAsync(channel, evt, CancellationToken.None);

        var session = await db.FlowSessions.SingleAsync();
        Assert.Equal(FlowSessionStatus.Waiting, session.Status);
        Assert.Equal(FlowWaitReason.CollectInput, session.WaitReason);
        Assert.Single(handler.Bodies);
        using (var doc = JsonDocument.Parse(handler.Bodies[0]))
            Assert.Equal("comment-1", doc.RootElement.GetProperty("recipient").GetProperty("comment_id").GetString());

        var contact = await db.Conversations.SingleAsync();

        // Қадами 2: корбар воқеан дар DM ҷавоб медиҳад — WebhookProcessor воқеӣ дар ин лаҳза
        // тирезаро мекушояд (ниг. ConversationWindowCalculator), пас аз он ProcessMessageAsync-ро
        // даъват мекунад. Аз ин ҷо минбаъд SendMessageAsync-и муқаррарӣ бояд кор кунад.
        contact.WindowExpiresAt = DateTimeOffset.UtcNow.AddHours(24);
        await db.SaveChangesAsync();
        var nameReply = new ParsedWebhookMessage(
            contact.ExternalId, contact.ContactUsername, null, "msg-name", MessageDirection.Inbound,
            MessageType.Text, "Фаридун", null, DateTimeOffset.UtcNow);
        await trigger.ProcessMessageAsync(channel, contact, nameReply, CancellationToken.None);

        await db.Entry(session).ReloadAsync();
        Assert.Equal(FlowSessionStatus.Waiting, session.Status);
        Assert.Equal(FlowWaitReason.CollectInput, session.WaitReason);
        Assert.Equal(2, handler.Bodies.Count); // "Ташаккур, Фаридун! Рақами телефон..."
        using (var doc = JsonDocument.Parse(handler.Bodies[1]))
        {
            Assert.Equal(contact.ExternalId, doc.RootElement.GetProperty("recipient").GetProperty("id").GetString());
            Assert.Contains("Фаридун", doc.RootElement.GetProperty("message").GetProperty("text").GetString());
        }

        // Қадами 3: рақами телефон — тег илова мешавад, паёми ниҳоӣ фиристода мешавад, сессия анҷом меёбад.
        var phoneReply = new ParsedWebhookMessage(
            contact.ExternalId, contact.ContactUsername, null, "msg-phone", MessageDirection.Inbound,
            MessageType.Text, "+992900000000", null, DateTimeOffset.UtcNow);
        await trigger.ProcessMessageAsync(channel, contact, phoneReply, CancellationToken.None);

        await db.Entry(session).ReloadAsync();
        Assert.Equal(FlowSessionStatus.Finished, session.Status);
        Assert.Equal(3, handler.Bodies.Count); // паёми ниҳоии "Ташаккур! Мо бо шумо тез тамос мегирем."
        Assert.Equal("lead_collected", (await db.ContactTags.SingleAsync()).Tag);
        Assert.Equal("+992900000000", (await db.ContactVariables.SingleAsync(v => v.Key == "phone")).Value);
    }
}
