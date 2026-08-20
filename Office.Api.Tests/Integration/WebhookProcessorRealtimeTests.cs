using System.Text.Json;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Office.Api.Channels;
using Office.Api.Channels.Facebook;
using Office.Api.Channels.Instagram;
using Office.Api.Channels.WhatsApp;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Realtime;

namespace Office.Api.Tests.Integration;

/// <summary>
/// The regression this guards against: "every unit test passes, but a real inbound webhook
/// never reaches the browser." Unlike every other test in this project, this one wires up a
/// real AppDbContext (EF Core InMemory — the first test here that needs actual LINQ queries
/// instead of faking the DB away) and drives WebhookProcessor.ProcessAsync end to end, exactly
/// the way Hangfire calls it from the webhook receive endpoint: raw JSON in, channel lookup,
/// idempotent upsert, and (the part a pure parser test can never prove) the realtime event
/// actually fires for the channel the message belongs to.
///
/// Only IChannelProvider's parse/id methods are faked (delegating straight to the real
/// Facebook/InstagramPayloadParser — no HTTP, no Meta) and IBackgroundJobClient is a
/// non-functional stub — neither is exercised by a plain text message, and asserting that
/// stays true (rather than silently skipping over it) is itself part of what this test checks.
/// </summary>
public class WebhookProcessorRealtimeTests
{
    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Channel MakeChannel(ChannelType type, string externalId) => new()
    {
        Id = Guid.CreateVersion7(),
        Type = type,
        Name = "Test channel",
        ExternalId = externalId,
        CredentialsEncrypted = "irrelevant-for-this-test",
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static WebhookLog MakeLog(string provider, string rawJson) => new()
    {
        Id = Guid.CreateVersion7(),
        Provider = provider,
        RawJson = rawJson,
        ReceivedAt = DateTimeOffset.UtcNow,
    };

    private const string InstagramTextMessagePayload = """
        {
          "object": "instagram",
          "entry": [
            {
              "id": "17841400000000000",
              "messaging": [
                {
                  "sender": { "id": "1254001234567890" },
                  "recipient": { "id": "17841400000000000" },
                  "timestamp": 1569262486134,
                  "message": { "mid": "aWdfZAG1fREALTIME", "text": "hello from Instagram" }
                }
              ]
            }
          ]
        }
        """;

    private const string FacebookTextMessagePayload = """
        {
          "object": "page",
          "entry": [
            {
              "id": "1234567890",
              "messaging": [
                {
                  "sender": { "id": "1000000000000001" },
                  "recipient": { "id": "1234567890" },
                  "timestamp": 1458692752478,
                  "message": { "mid": "mid.REALTIME", "text": "hello from Facebook" }
                }
              ]
            }
          ]
        }
        """;

    [Fact]
    public async Task ProcessAsync_InstagramInboundMessage_PublishesMessageReceivedForTheOwningChannel()
    {
        await using var db = CreateDb();
        var channel = MakeChannel(ChannelType.Instagram, "17841400000000000");
        db.Channels.Add(channel);
        var log = MakeLog("Instagram", InstagramTextMessagePayload);
        db.WebhookLogs.Add(log);
        await db.SaveChangesAsync();

        var events = new RecordingInboxEventPublisher();
        var processor = new WebhookProcessor(
            db, new FakeChannelProviderFactory(), events, new NonFunctionalBackgroundJobClient(), NullLogger<WebhookProcessor>.Instance);

        await processor.ProcessAsync(log.Id, CancellationToken.None);

        // Sanity first: if this fails, the assertions below about the event would be checking
        // the wrong thing (a processing failure that happened to still leave Calls empty).
        Assert.Null(log.Error);

        var received = Assert.Single(events.Calls, c => c.EventName == "MessageReceived");
        Assert.Equal(channel.Id, received.ChannelId);

        var savedMessage = await db.Messages.SingleAsync();
        Assert.Equal("hello from Instagram", savedMessage.Body);
        Assert.Equal("aWdfZAG1fREALTIME", savedMessage.ExternalId);
    }

    [Fact]
    public async Task ProcessAsync_FacebookInboundMessage_PublishesMessageReceivedForTheOwningChannel()
    {
        // Same path, WhatsApp's sibling providers — proves the realtime publish isn't an
        // Instagram-specific fluke either way (accidentally right or accidentally wrong).
        await using var db = CreateDb();
        var channel = MakeChannel(ChannelType.Facebook, "1234567890");
        db.Channels.Add(channel);
        var log = MakeLog("Facebook", FacebookTextMessagePayload);
        db.WebhookLogs.Add(log);
        await db.SaveChangesAsync();

        var events = new RecordingInboxEventPublisher();
        var processor = new WebhookProcessor(
            db, new FakeChannelProviderFactory(), events, new NonFunctionalBackgroundJobClient(), NullLogger<WebhookProcessor>.Instance);

        await processor.ProcessAsync(log.Id, CancellationToken.None);

        Assert.Null(log.Error);
        var received = Assert.Single(events.Calls, c => c.EventName == "MessageReceived");
        Assert.Equal(channel.Id, received.ChannelId);
    }

    [Fact]
    public async Task ProcessAsync_InstagramInboundMessage_PublishesToTheCorrectChannelWhenSeveralExist()
    {
        // The exact bug shape a duplicate/stale channel row would cause: the message gets
        // saved (so "it works after a refresh") but the realtime event targets group
        // channel:{wrong-id} — nobody watching the right one ever sees it live.
        await using var db = CreateDb();
        var wrongChannel = MakeChannel(ChannelType.Instagram, "99999999999999999");
        var rightChannel = MakeChannel(ChannelType.Instagram, "17841400000000000");
        db.Channels.AddRange(wrongChannel, rightChannel);
        var log = MakeLog("Instagram", InstagramTextMessagePayload);
        db.WebhookLogs.Add(log);
        await db.SaveChangesAsync();

        var events = new RecordingInboxEventPublisher();
        var processor = new WebhookProcessor(
            db, new FakeChannelProviderFactory(), events, new NonFunctionalBackgroundJobClient(), NullLogger<WebhookProcessor>.Instance);

        await processor.ProcessAsync(log.Id, CancellationToken.None);

        Assert.Null(log.Error);
        var received = Assert.Single(events.Calls, c => c.EventName == "MessageReceived");
        Assert.Equal(rightChannel.Id, received.ChannelId);
        Assert.NotEqual(wrongChannel.Id, received.ChannelId);
    }

    private sealed record RecordedEvent(string EventName, Guid ChannelId, Guid? AssignedTo);

    private sealed class RecordingInboxEventPublisher : IInboxEventPublisher
    {
        public List<RecordedEvent> Calls { get; } = [];

        public Task MessageReceivedAsync(Guid channelId, Guid? assignedTo, object payload, CancellationToken ct)
        {
            Calls.Add(new RecordedEvent("MessageReceived", channelId, assignedTo));
            return Task.CompletedTask;
        }

        public Task MessageSentAsync(Guid channelId, Guid? assignedTo, object payload, CancellationToken ct)
        {
            Calls.Add(new RecordedEvent("MessageSent", channelId, assignedTo));
            return Task.CompletedTask;
        }

        public Task ConversationAssignedAsync(Guid channelId, Guid? assignedTo, object payload, CancellationToken ct)
        {
            Calls.Add(new RecordedEvent("ConversationAssigned", channelId, assignedTo));
            return Task.CompletedTask;
        }

        public Task ConversationStatusChangedAsync(Guid channelId, Guid? assignedTo, object payload, CancellationToken ct)
        {
            Calls.Add(new RecordedEvent("ConversationStatusChanged", channelId, assignedTo));
            return Task.CompletedTask;
        }
    }

    /// <summary>Parsing only (real Facebook/InstagramPayloadParser, no HTTP) — every send/media method throws if called, since a plain text message never reaches them.</summary>
    private sealed class FakeChannelProviderFactory : IChannelProviderFactory
    {
        public IChannelProvider GetProvider(ChannelType type) => type switch
        {
            ChannelType.Instagram => new FakeInstagramProvider(),
            ChannelType.Facebook => new FakeFacebookProvider(),
            _ => throw new NotSupportedException($"Ин тест барои '{type}' омода нашудааст."),
        };
    }

    private sealed class FakeInstagramProvider : IChannelProvider
    {
        public bool VerifyWebhookToken(string verifyToken) => throw new NotSupportedException();
        public string? ExtractChannelExternalId(JsonElement payload) => InstagramPayloadParser.ExtractChannelExternalId(payload);

        public Task<IReadOnlyList<ParsedWebhookMessage>> ParseWebhookAsync(Channel channel, JsonElement payload, CancellationToken ct) =>
            Task.FromResult(InstagramPayloadParser.ParseMessages(payload));

        public Task<IReadOnlyList<ParsedStatusUpdate>> ParseStatusUpdatesAsync(Channel channel, JsonElement payload, CancellationToken ct) =>
            Task.FromResult(InstagramPayloadParser.ParseStatusUpdates(payload));

        public Task<string?> SendMessageAsync(Channel channel, string conversationExternalId, string body, string? messageTag, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<string?> SendTemplateAsync(
            Channel channel, string conversationExternalId, string templateName, string languageCode,
            IReadOnlyList<string> parameters, CancellationToken ct) => throw new NotSupportedException();

        public Task MarkAsReadAsync(Channel channel, string messageExternalId, CancellationToken ct) => throw new NotSupportedException();
        public Task<Stream> DownloadMediaAsync(Channel channel, string mediaExternalId, CancellationToken ct) => throw new NotSupportedException();

        public Task<string> UploadMediaAsync(Channel channel, Stream content, string mimeType, string fileName, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<string?> SendMediaMessageAsync(
            Channel channel, string conversationExternalId, string mediaExternalId, MessageType type,
            string? caption, bool isVoiceNote, string? messageTag, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<WhatsAppTemplateInfo>> GetApprovedTemplatesAsync(Channel channel, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<ContactProfile> GetContactProfileAsync(Channel channel, string contactExternalId, CancellationToken ct) =>
            Task.FromResult(ContactProfile.Empty);
    }

    private sealed class FakeFacebookProvider : IChannelProvider
    {
        public bool VerifyWebhookToken(string verifyToken) => throw new NotSupportedException();
        public string? ExtractChannelExternalId(JsonElement payload) => FacebookPayloadParser.ExtractChannelExternalId(payload);

        public Task<IReadOnlyList<ParsedWebhookMessage>> ParseWebhookAsync(Channel channel, JsonElement payload, CancellationToken ct) =>
            Task.FromResult(FacebookPayloadParser.ParseMessages(payload));

        public Task<IReadOnlyList<ParsedStatusUpdate>> ParseStatusUpdatesAsync(Channel channel, JsonElement payload, CancellationToken ct) =>
            Task.FromResult(FacebookPayloadParser.ParseStatusUpdates(payload));

        public Task<string?> SendMessageAsync(Channel channel, string conversationExternalId, string body, string? messageTag, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<string?> SendTemplateAsync(
            Channel channel, string conversationExternalId, string templateName, string languageCode,
            IReadOnlyList<string> parameters, CancellationToken ct) => throw new NotSupportedException();

        public Task MarkAsReadAsync(Channel channel, string messageExternalId, CancellationToken ct) => throw new NotSupportedException();
        public Task<Stream> DownloadMediaAsync(Channel channel, string mediaExternalId, CancellationToken ct) => throw new NotSupportedException();

        public Task<string> UploadMediaAsync(Channel channel, Stream content, string mimeType, string fileName, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<string?> SendMediaMessageAsync(
            Channel channel, string conversationExternalId, string mediaExternalId, MessageType type,
            string? caption, bool isVoiceNote, string? messageTag, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<WhatsAppTemplateInfo>> GetApprovedTemplatesAsync(Channel channel, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<ContactProfile> GetContactProfileAsync(Channel channel, string contactExternalId, CancellationToken ct) =>
            Task.FromResult(ContactProfile.Empty);
    }

    /// <summary>Never actually called by a plain text message (no media => no MediaDownloadJob enqueue) — throws if that assumption ever stops holding.</summary>
    private sealed class NonFunctionalBackgroundJobClient : Hangfire.IBackgroundJobClient
    {
        public string Create(Job job, IState state) => throw new NotSupportedException("Ин тест enqueue-и job интизор надорад — паёми матнӣ медиа надорад.");
        public bool ChangeState(string jobId, IState state, string? expectedState) => throw new NotSupportedException();
    }
}
