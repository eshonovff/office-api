using System.Text.Json;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Office.Api.Channels;
using Office.Api.Channels.Automation;
using Office.Api.Channels.Comments;
using Office.Api.Channels.ContactProfiles;
using Office.Api.Channels.Flows;
using Office.Api.Channels.Instagram;
using Office.Api.Channels.WhatsApp;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Realtime;
using Office.Api.Tests.Channels.Comments;

namespace Office.Api.Tests.Integration;

/// <summary>
/// What the user saw: "people I write to first on Instagram get no name or picture". The account
/// writes first (an echo makes the chat); Instagram tells nothing about the person yet; when they
/// write back, the system must ask again — the real WebhookProcessor, the real Instagram parser.
/// </summary>
public class ContactProfileWebhookTests
{
    private const string Account = "17841400000000000";
    private const string Fan = "1069591212724119";
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Channel _channel = new()
    {
        Id = Guid.CreateVersion7(), Type = ChannelType.Instagram, Name = "eshonov.f1", ExternalId = Account,
        CredentialsEncrypted = "x", IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
    };
    private readonly ProfileAwareInstagram _instagram = new();
    private readonly RecordingJobs _jobs;
    private int _mid;

    public ContactProfileWebhookTests()
    {
        _db.Channels.Add(_channel);
        _db.SaveChanges();
        _jobs = new RecordingJobs(_db);
    }

    private async Task Deliver(bool fromAccount)
    {
        var sender = fromAccount ? Account : Fan;
        var recipient = fromAccount ? Fan : Account;
        var echo = fromAccount ? ", \"is_echo\": true" : "";
        var json = $$$"""
            {"object":"instagram","entry":[{"id":"{{{Account}}}","messaging":[{"sender":{"id":"{{{sender}}}"},"recipient":{"id":"{{{recipient}}}"},
             "timestamp":{{{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}}},"message":{"mid":"mid-{{{++_mid}}}","text":"салом"{{{echo}}}}}]}]}
            """;
        var log = new WebhookLog { Id = Guid.CreateVersion7(), Provider = "Instagram", RawJson = json, ReceivedAt = DateTimeOffset.UtcNow };
        _db.WebhookLogs.Add(log);
        await _db.SaveChangesAsync();

        var processor = new WebhookProcessor(
            _db, new Factory(_instagram), new NoEvents(), _jobs,
            new CommentAutomationProcessor(_db, _jobs, NullLogger<CommentAutomationProcessor>.Instance),
            MakeFlowTrigger(_db),
            new CommentStore(_db, new RecordingCommentEventPublisher(), NullLogger<CommentStore>.Instance),
            NullLogger<WebhookProcessor>.Instance);
        await processor.ProcessAsync(log.Id, CancellationToken.None);
        Assert.Null(log.Error);
    }

    private Conversation Chat() => _db.Conversations.AsNoTracking().Single();

    private async Task Rewind(TimeSpan by)
    {
        var chat = await _db.Conversations.SingleAsync();
        chat.ContactProfileFetchedAt -= by;
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task TheAccountWritesFirst_ThePersonReplies_NowTheyHaveANameAndAPicture()
    {
        await Deliver(fromAccount: true);
        Assert.Equal([Fan], _instagram.Asked); // asked when the chat was made — Instagram said nothing yet
        Assert.Equal((null, null), (Chat().ContactName, Chat().ContactUsername));
        Assert.Empty(_jobs.PicturesQueued);

        _instagram.HasConsent = true;
        await Rewind(TimeSpan.FromMinutes(2));
        await Deliver(fromAccount: false);

        Assert.Equal(2, _instagram.Asked.Count);
        Assert.Equal(("Mir777 naja", "mir777naja"), (Chat().ContactName, Chat().ContactUsername));
        Assert.Equal([Chat().Id], _jobs.PicturesQueued);
        Assert.True(_jobs.LinkWasSavedWhenQueued); // the picture job reads the saved link
    }

    [Fact]
    public async Task TheAccountsOwnMessages_NeverAskAgain()
    {
        await Deliver(fromAccount: true);
        _instagram.HasConsent = true;
        await Rewind(TimeSpan.FromMinutes(5));

        await Deliver(fromAccount: true);
        await Deliver(fromAccount: true);

        Assert.Single(_instagram.Asked);
    }

    [Fact]
    public async Task ABurstOfMessages_AsksAtMostOnceAMinute()
    {
        await Deliver(fromAccount: true);

        await Deliver(fromAccount: false);
        await Deliver(fromAccount: false);

        Assert.Single(_instagram.Asked);
    }

    [Fact]
    public async Task AKnownPersonWithAPicture_IsRefreshedWeekly_NotOnEveryMessage()
    {
        _instagram.HasConsent = true;
        await Deliver(fromAccount: false);
        var chat = await _db.Conversations.SingleAsync();
        chat.ContactAvatarPath = "whatsapp-media/x/avatars/p.jpg";
        await _db.SaveChangesAsync();

        await Rewind(TimeSpan.FromDays(3));
        await Deliver(fromAccount: false);
        Assert.Single(_instagram.Asked);

        await Rewind(TimeSpan.FromDays(5));
        await Deliver(fromAccount: false);
        Assert.Equal(2, _instagram.Asked.Count);
    }

    private sealed class ProfileAwareInstagram : IChannelProvider
    {
        public bool HasConsent { get; set; }
        public List<string> Asked { get; } = [];

        public Task<ContactProfile> GetContactProfileAsync(Channel channel, string contactExternalId, CancellationToken ct)
        {
            Asked.Add(contactExternalId);
            return Task.FromResult(HasConsent
                ? new ContactProfile("Mir777 naja", "https://scontent.cdninstagram.com/p.jpg", "mir777naja")
                : ContactProfile.Empty);
        }

        public string? ExtractChannelExternalId(JsonElement payload) => InstagramPayloadParser.ExtractChannelExternalId(payload);
        public Task<IReadOnlyList<ParsedWebhookMessage>> ParseWebhookAsync(Channel channel, JsonElement payload, CancellationToken ct) =>
            Task.FromResult(InstagramPayloadParser.ParseMessages(payload));
        public Task<IReadOnlyList<ParsedStatusUpdate>> ParseStatusUpdatesAsync(Channel channel, JsonElement payload, CancellationToken ct) =>
            Task.FromResult(InstagramPayloadParser.ParseStatusUpdates(payload));
        public bool VerifyWebhookToken(string verifyToken) => throw new NotSupportedException();
        public Task<string?> SendMessageAsync(Channel channel, string conversationExternalId, string body, string? messageTag, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> SendTemplateAsync(Channel channel, string conversationExternalId, string templateName, string languageCode, IReadOnlyList<string> parameters, CancellationToken ct) => throw new NotSupportedException();
        public Task MarkAsReadAsync(Channel channel, string messageExternalId, CancellationToken ct) => throw new NotSupportedException();
        public Task<DownloadedMedia> DownloadMediaAsync(Channel channel, string mediaExternalId, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> UploadMediaAsync(Channel channel, Stream content, string mimeType, string fileName, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> SendMediaMessageAsync(Channel channel, string conversationExternalId, string mediaExternalId, MessageType type, string? caption, bool isVoiceNote, string? messageTag, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<WhatsAppTemplateInfo>> GetApprovedTemplatesAsync(Channel channel, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Factory(IChannelProvider instagram) : IChannelProviderFactory
    {
        public IChannelProvider GetProvider(ChannelType type) => type == ChannelType.Instagram ? instagram : throw new NotSupportedException();
    }

    /// <summary>Records the picture jobs — and whether the link was already saved when each was queued.</summary>
    private sealed class RecordingJobs(AppDbContext db) : IBackgroundJobClient
    {
        public List<Guid> PicturesQueued { get; } = [];
        public bool LinkWasSavedWhenQueued { get; private set; }

        public string Create(Job job, IState state)
        {
            if (job.Type == typeof(ContactAvatarJob))
            {
                var id = (Guid)job.Args[0]!;
                PicturesQueued.Add(id);
                LinkWasSavedWhenQueued = db.Conversations.AsNoTracking().Any(c => c.Id == id && c.ContactAvatarUrl != null);
            }
            return "job";
        }

        public bool ChangeState(string jobId, IState state, string? expectedState) => true;
    }

    private sealed class NoEvents : IInboxEventPublisher
    {
        public Task MessageReceivedAsync(Guid channelId, Guid? assignedTo, object payload, CancellationToken ct) => Task.CompletedTask;
        public Task MessageSentAsync(Guid channelId, Guid? assignedTo, object payload, CancellationToken ct) => Task.CompletedTask;
        public Task ConversationAssignedAsync(Guid channelId, Guid? assignedTo, object payload, CancellationToken ct) => Task.CompletedTask;
        public Task ConversationStatusChangedAsync(Guid channelId, Guid? assignedTo, object payload, CancellationToken ct) => Task.CompletedTask;
    }

    private static FlowTriggerProcessor MakeFlowTrigger(AppDbContext db)
    {
        var http = new HttpClient(new NoHttp());
        var provider = new InstagramProvider(
            http, new Passthrough(), new ConfigurationBuilder().Build(), db, new NoNotifications(),
            new MemoryCache(new MemoryCacheOptions()), new InstagramFollowCheckRateLimiter(), NullLogger<InstagramProvider>.Instance);
        var engine = new FlowEngine(db, provider, new NoJobs(), http, NullLogger<FlowEngine>.Instance);
        return new FlowTriggerProcessor(db, engine, NullLogger<FlowTriggerProcessor>.Instance);
    }

    private sealed class NoJobs : IBackgroundJobClient
    {
        public string Create(Job job, IState state) => throw new NotSupportedException();
        public bool ChangeState(string jobId, IState state, string? expectedState) => throw new NotSupportedException();
    }

    private sealed class NoHttp : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Passthrough : IChannelCredentialsProtector
    {
        public string Protect(string plainText) => plainText;
        public string Unprotect(string protectedText) => protectedText;
    }

    private sealed class NoNotifications : INotificationService
    {
        public Task PushAsync(Guid userId, string type, object payload, CancellationToken ct) => Task.CompletedTask;
    }
}
