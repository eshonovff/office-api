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
using Office.Api.Media;
using Office.Api.Realtime;

using Office.Api.Tests.Channels.Comments;

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

    /// <summary>AttachDefaultImagesAsync-ро caller-и он ба ffmpeg-и воқеӣ вобаста намекунад
    /// (thumbnail — best-effort, ноком шудани он паёми асосиро намебандад) — ин ҷо ҳамон
    /// алгуи MediaDownloadJobContentTypeTests/MediaSendJobTests: NotSupportedException.</summary>
    private class NoOpMediaProcessor : IMediaProcessor
    {
        public Task TranscodeToOggOpusAsync(string inputPath, string outputPath, CancellationToken ct) => throw new NotSupportedException();
        public Task TranscodeToAacAsync(string inputPath, string outputPath, CancellationToken ct) => throw new NotSupportedException();
        public Task<int?> GetAudioDurationSecondsAsync(string inputPath, CancellationToken ct) => throw new NotSupportedException();
        public virtual Task GenerateImageThumbnailAsync(string inputPath, string outputPath, int maxDimension, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<short>> GenerateWaveformPeaksAsync(string inputPath, int peakCount, CancellationToken ct) => throw new NotSupportedException();
    }

    /// <summary>ffmpeg-и воқеиро тақлид мекунад — байтҳои сохта ба outputPath менависад, то
    /// PreviewDataUri-и натиҷа санҷида шавад, бе ниёз ба ffmpeg-и воқеӣ дар CI.</summary>
    private sealed class StubThumbnailMediaProcessor : NoOpMediaProcessor
    {
        public override async Task GenerateImageThumbnailAsync(string inputPath, string outputPath, int maxDimension, CancellationToken ct) =>
            await File.WriteAllBytesAsync(outputPath, [0xFF, 0xD8, 0xFF, 0xD9], ct); // сарлавҳа/интиҳои JPEG — контенти воқеӣ лозим нест
    }

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
        var trigger = new FlowTriggerProcessor(db, engine, new RecordingPublicReplyScheduler(), NullLogger<FlowTriggerProcessor>.Instance);
        return (handler, trigger);
    }

    /// <summary>
    /// Like MakeTrigger, but Instagram's follow check (GET ?fields=...is_user_follow_business)
    /// answers from <paramref name="isFollowing"/>, read on every call — a "not following" answer
    /// is never cached, so a test can flip it between steps. Sends are the requests with a body.
    /// </summary>
    private static (FakeHttpMessageHandler Handler, FlowTriggerProcessor Trigger, FlowEngine Engine) MakeTriggerWithFollowCheck(
        AppDbContext db, Func<bool> isFollowing)
    {
        var handler = new FakeHttpMessageHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                request.Method == HttpMethod.Get && request.RequestUri!.Query.Contains("is_user_follow_business")
                    ? $$"""{"username":"fan","is_user_follow_business":{{(isFollowing() ? "true" : "false")}}}"""
                    : """{"message_id":"mid.123"}""",
                Encoding.UTF8, "application/json"),
        });
        var provider = new InstagramProvider(
            new HttpClient(handler), new PassthroughProtector(), new ConfigurationBuilder().Build(), db,
            new NoOpNotificationService(), new MemoryCache(new MemoryCacheOptions()),
            new InstagramFollowCheckRateLimiter(), NullLogger<InstagramProvider>.Instance);
        var engine = new FlowEngine(db, provider, new RecordingBackgroundJobClient(), new HttpClient(handler), NullLogger<FlowEngine>.Instance);
        var trigger = new FlowTriggerProcessor(db, engine, new RecordingPublicReplyScheduler(), NullLogger<FlowTriggerProcessor>.Instance);
        return (handler, trigger, engine);
    }

    private static List<JsonElement> Sends(FakeHttpMessageHandler handler) =>
        handler.Bodies.Where(b => b.Length > 0).Select(b => JsonDocument.Parse(b).RootElement.Clone()).ToList();

    /// <summary>Every string in a send, decoded — a message with a button nests its text deeper than a plain one.</summary>
    private static string AllText(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString()!,
        JsonValueKind.Object => string.Join(" ", element.EnumerateObject().Select(p => AllText(p.Value))),
        JsonValueKind.Array => string.Join(" ", element.EnumerateArray().Select(AllText)),
        _ => "",
    };

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

    /// <summary>
    /// FlowTemplateInstantiator.AttachDefaultImagesAsync (2026-09-17, дархости корбар: "дар якум
    /// смс по умолчанию ягон сурат мон") — attachment_id-и Meta умумӣ буда наметавонад, пас файли
    /// Assets/-ро барои КАНАЛИ мушаххас бор мекунад ва натиҷаро ба блоки паём замима мекунад.
    /// </summary>
    [Fact]
    public async Task AttachDefaultImagesAsync_UploadsAssetAndAttachesImageBlockToNode()
    {
        var assetDir = Path.Combine(AppContext.BaseDirectory, "Assets", "DefaultTemplateImages");
        Directory.CreateDirectory(assetDir);
        var assetPath = Path.Combine(assetDir, "test-lead-magnet.png");
        await File.WriteAllBytesAsync(assetPath, [0x89, 0x50, 0x4E, 0x47]); // сарлавҳаи PNG кофист — воқеан рамзгузорӣ намекунем
        try
        {
            var channel = MakeChannel();
            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"attachment_id":"fake_attach_123"}""", Encoding.UTF8, "application/json"),
            });
            var provider = new InstagramProvider(
                new HttpClient(handler), new PassthroughProtector(), new ConfigurationBuilder().Build(), CreateDb(),
                new NoOpNotificationService(), new MemoryCache(new MemoryCacheOptions()),
                new InstagramFollowCheckRateLimiter(), NullLogger<InstagramProvider>.Instance);

            var introKey = "intro";
            var definition = new FlowTemplateDefinition(
                [new FlowTemplateNodeDefinition(introKey, "message",
                    JsonSerializer.SerializeToElement(new MessageNodeConfig([new MessageBlock(MessageBlock.TypeText, "Салом!", null)], []), FlowJsonOptions.Options),
                    0, 0, DefaultImageAsset: "test-lead-magnet.png")],
                []);

            var (nodes, _) = FlowTemplateInstantiator.Instantiate(definition, Guid.CreateVersion7());
            await FlowTemplateInstantiator.AttachDefaultImagesAsync(definition, nodes, channel, provider, new NoOpMediaProcessor(), NullLogger<InstagramProvider>.Instance, CancellationToken.None);

            Assert.Single(handler.Bodies);
            Assert.Contains("is_reusable", handler.Bodies[0]); // қисми JSON-и multipart body-и UploadMediaAsync

            var config = JsonSerializer.Deserialize<MessageNodeConfig>(nodes.Single().ConfigJson, FlowJsonOptions.Options)!;
            var imageBlock = Assert.Single(config.Blocks, b => b.Type == MessageBlock.TypeImage);
            Assert.Equal("fake_attach_123", imageBlock.MediaId);
            Assert.Contains(config.Blocks, b => b.Type == MessageBlock.TypeText); // матни аслӣ гум нашуд
            // NoOpMediaProcessor.GenerateImageThumbnailAsync хато медиҳад — расм бояд ҳамоно
            // замима шавад (attachment_id аз он вобаста нест), танҳо PreviewDataUri холӣ мемонад.
            Assert.Null(imageBlock.PreviewDataUri);
        }
        finally
        {
            File.Delete(assetPath);
        }
    }

    /// <summary>Регрессия барои MessageBlock.PreviewDataUri: агар сохтани thumbnail муваффақ
    /// шавад, натиҷа (data URI) дар config-и нод захира мешавад — то фронтенд баъд аз reload
    /// низ расмро (бе такя ба MediaId-и опаку) нишон дода тавонад.</summary>
    [Fact]
    public async Task AttachDefaultImagesAsync_ThumbnailGenerationSucceeds_StoresPreviewDataUriOnBlock()
    {
        var assetDir = Path.Combine(AppContext.BaseDirectory, "Assets", "DefaultTemplateImages");
        Directory.CreateDirectory(assetDir);
        var assetPath = Path.Combine(assetDir, "test-lead-magnet-2.png");
        await File.WriteAllBytesAsync(assetPath, [0x89, 0x50, 0x4E, 0x47]);
        try
        {
            var channel = MakeChannel();
            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"attachment_id":"fake_attach_456"}""", Encoding.UTF8, "application/json"),
            });
            var provider = new InstagramProvider(
                new HttpClient(handler), new PassthroughProtector(), new ConfigurationBuilder().Build(), CreateDb(),
                new NoOpNotificationService(), new MemoryCache(new MemoryCacheOptions()),
                new InstagramFollowCheckRateLimiter(), NullLogger<InstagramProvider>.Instance);

            var definition = new FlowTemplateDefinition(
                [new FlowTemplateNodeDefinition("intro", "message",
                    JsonSerializer.SerializeToElement(new MessageNodeConfig([new MessageBlock(MessageBlock.TypeText, "Салом!", null)], []), FlowJsonOptions.Options),
                    0, 0, DefaultImageAsset: "test-lead-magnet-2.png")],
                []);

            var (nodes, _) = FlowTemplateInstantiator.Instantiate(definition, Guid.CreateVersion7());
            await FlowTemplateInstantiator.AttachDefaultImagesAsync(
                definition, nodes, channel, provider, new StubThumbnailMediaProcessor(), NullLogger<InstagramProvider>.Instance, CancellationToken.None);

            var config = JsonSerializer.Deserialize<MessageNodeConfig>(nodes.Single().ConfigJson, FlowJsonOptions.Options)!;
            var imageBlock = Assert.Single(config.Blocks, b => b.Type == MessageBlock.TypeImage);
            Assert.Equal("fake_attach_456", imageBlock.MediaId);
            Assert.NotNull(imageBlock.PreviewDataUri);
            Assert.StartsWith("data:image/jpeg;base64,", imageBlock.PreviewDataUri);
        }
        finally
        {
            File.Delete(assetPath);
        }
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

    private const string FollowTemplate = "Ҷавоб ба шарҳ бо санҷиши обуна";

    [Fact]
    public async Task FollowCheckedReply_CommentFromFollower_GetsTheFollowersMessageAsThePrivateReply()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        db.Channels.Add(channel);
        await db.SaveChangesAsync();
        await InstantiateTemplateAsync(db, channel.Id, FollowTemplate, "instagram_comment", CancellationToken.None);
        var (handler, trigger, _) = MakeTriggerWithFollowCheck(db, () => true);

        await trigger.ProcessCommentAsync(channel, new ParsedCommentEvent("comment-1", "1254001234567890", "fan", "нарх?", "media-1"), CancellationToken.None);

        Assert.Equal(FlowSessionStatus.Finished, (await db.FlowSessions.SingleAsync()).Status);
        var send = Assert.Single(Sends(handler));
        Assert.Equal("comment-1", send.GetProperty("recipient").GetProperty("comment_id").GetString());
        Assert.Contains("обуначии мо ҳастед", AllText(send));
    }

    [Fact]
    public async Task FollowCheckedReply_NonFollower_IsAskedToFollow_ThenGetsTheFollowersMessageAfterFollowingAndTapping()
    {
        await using var db = CreateDb();
        var channel = MakeChannel();
        db.Channels.Add(channel);
        await db.SaveChangesAsync();
        await InstantiateTemplateAsync(db, channel.Id, FollowTemplate, "instagram_comment", CancellationToken.None);
        var following = false;
        var (handler, trigger, engine) = MakeTriggerWithFollowCheck(db, () => following);

        // 1. Not a follower: the "please follow" message, with its button, is the comment's one private reply.
        await trigger.ProcessCommentAsync(channel, new ParsedCommentEvent("comment-1", "1254001234567890", "fan", "нарх?", "media-1"), CancellationToken.None);

        var session = await db.FlowSessions.SingleAsync();
        Assert.Equal(FlowSessionStatus.Waiting, session.Status);
        Assert.Equal(FlowWaitReason.ButtonClick, session.WaitReason);
        var ask = Assert.Single(Sends(handler));
        Assert.Equal("comment-1", ask.GetProperty("recipient").GetProperty("comment_id").GetString());
        Assert.Contains("обуна шавед", AllText(ask));
        Assert.Contains($"{session.Id}:{session.CurrentNodeId}:0", AllText(ask));

        // 2. Taps "Обуна шудам ✅" without following: asked again — now a normal DM, the tap opened the window.
        var contact = await db.Conversations.SingleAsync();
        contact.WindowExpiresAt = DateTimeOffset.UtcNow.AddHours(24);
        await db.SaveChangesAsync();
        await engine.ResumeFromButtonAsync($"{session.Id}:{session.CurrentNodeId}:0", CancellationToken.None);

        await db.Entry(session).ReloadAsync();
        Assert.Equal(FlowWaitReason.ButtonClick, session.WaitReason);
        Assert.Equal(2, Sends(handler).Count);
        Assert.Equal(contact.ExternalId, Sends(handler)[1].GetProperty("recipient").GetProperty("id").GetString());
        Assert.Contains("обуна шавед", AllText(Sends(handler)[1]));

        // 3. Follows, taps again: checked afresh ("not following" is never cached) → the followers' message.
        following = true;
        await engine.ResumeFromButtonAsync($"{session.Id}:{session.CurrentNodeId}:0", CancellationToken.None);

        await db.Entry(session).ReloadAsync();
        Assert.Equal(FlowSessionStatus.Finished, session.Status);
        var sends = Sends(handler);
        Assert.Equal(3, sends.Count);
        Assert.Equal(contact.ExternalId, sends[2].GetProperty("recipient").GetProperty("id").GetString());
        Assert.Contains("обуначии мо ҳастед", AllText(sends[2]));
    }

    [Fact]
    public async Task FollowCheckedReply_StartsAtTheFirstCheck_TheLoopGoesThroughTheSecond()
    {
        // The engine starts at the only node nothing points to; a loop back into the first check
        // would leave no start at all.
        await using var db = CreateDb();
        await FlowTemplateSeeder.SeedAsync(db, CancellationToken.None);
        var template = await db.FlowTemplates.SingleAsync(t => t.Name == FollowTemplate);
        var definition = JsonSerializer.Deserialize<FlowTemplateDefinition>(template.DefinitionJson)!;

        var starts = definition.Nodes.Where(n => definition.Edges.All(e => e.ToKey != n.Key)).Select(n => n.Key).ToList();
        Assert.Equal(["check"], starts);
    }
}

