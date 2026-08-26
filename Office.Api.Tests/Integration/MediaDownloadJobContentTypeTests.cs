using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Office.Api.Channels;
using Office.Api.Channels.WhatsApp;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Media;
using Office.Api.Realtime;

namespace Office.Api.Tests.Integration;

/// <summary>
/// The exact regression: a Meta CDN url that no longer resolves to real media returned
/// HTTP 200 with a Content-Type: text/html error page instead of 404/403 — and
/// response.EnsureSuccessStatusCode() alone doesn't know the difference. That page got
/// written to disk, the message.MimeType was left null, and the operator saw a black player
/// with no error — for two days, across every channel, before anyone noticed. This is the
/// test for exactly that: HTTP 200 + text/html must never be treated as a successful download.
/// </summary>
public class MediaDownloadJobContentTypeTests
{
    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static (AppDbContext Db, Message Message, Channel Channel) SeedMessage(MessageType type)
    {
        var db = CreateDb();
        var channel = new Channel
        {
            Id = Guid.CreateVersion7(),
            Type = ChannelType.Instagram,
            Name = "Test channel",
            ExternalId = "17841400000000000",
            CredentialsEncrypted = "irrelevant-for-this-test",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var conversation = new Conversation
        {
            Id = Guid.CreateVersion7(),
            ChannelId = channel.Id,
            Channel = channel,
            ExternalId = "1254001234567890",
            Status = ConversationStatus.New,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var message = new Message
        {
            Id = Guid.CreateVersion7(),
            ConversationId = conversation.Id,
            Conversation = conversation,
            Direction = MessageDirection.Inbound,
            Type = type,
            DeliveryStatus = MessageDeliveryStatus.Delivered,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Channels.Add(channel);
        db.Conversations.Add(conversation);
        db.Messages.Add(message);
        db.SaveChanges();
        return (db, message, channel);
    }

    private static MediaDownloadJob MakeJob(AppDbContext db, IChannelProvider provider) =>
        new(
            db,
            new SingleProviderFactory(provider),
            new ThrowingMediaProcessor(),
            new ConfigurationBuilder().Build(),
            new FakeWebHostEnvironment(),
            new NoOpInboxEventPublisher(),
            NullLogger<MediaDownloadJob>.Instance);

    [Fact]
    public async Task DownloadAsync_HttpOkWithHtmlContentType_IsTreatedAsAnErrorNotASuccess()
    {
        var (db, message, _) = SeedMessage(MessageType.Image);
        await using var dbScope = db;

        var htmlBody = Encoding.UTF8.GetBytes("<!doctype html><html><body>Link expired</body></html>");
        var provider = new FakeDownloadProvider(new DownloadedMedia(new MemoryStream(htmlBody), "text/html"));
        var job = MakeJob(db, provider);

        await job.DownloadAsync(message.Id, "https://scontent.cdninstagram.com/expired.jpg", CancellationToken.None);

        var reloaded = await db.Messages.SingleAsync();
        Assert.NotNull(reloaded.MediaDownloadError);
        Assert.Null(reloaded.MediaUrl);
        Assert.Null(reloaded.MimeType);
    }

    [Fact]
    public async Task DownloadAsync_HtmlContentType_DoesNotThrow_SoHangfireNeverRetriesAnExpiredUrl()
    {
        // If this threw, [AutomaticRetry] would retry up to 5 times over 6 hours against the
        // exact same already-expired url — every retry would fail identically. Retrying only
        // makes sense for transient errors (network blips, Meta 5xx); an HTML error page for a
        // url that will never change is a terminal outcome, not a transient one.
        var (db, message, _) = SeedMessage(MessageType.Video);
        await using var dbScope = db;

        var provider = new FakeDownloadProvider(new DownloadedMedia(new MemoryStream("<html></html>"u8.ToArray()), "text/html"));
        var job = MakeJob(db, provider);

        var exception = await Record.ExceptionAsync(
            () => job.DownloadAsync(message.Id, "https://scontent.xx.fbcdn.net/expired.mp4", CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task DownloadAsync_RealContentType_SetsMimeTypeFromTheDownloadResponse()
    {
        // Facebook/Instagram webhooks never carry mime_type (unlike WhatsApp's contacts[]) — the
        // download response's own Content-Type is the only place this can come from.
        var (db, message, _) = SeedMessage(MessageType.Video);
        await using var dbScope = db;

        var provider = new FakeDownloadProvider(new DownloadedMedia(new MemoryStream([1, 2, 3, 4]), "video/mp4"));
        var job = MakeJob(db, provider);

        await job.DownloadAsync(message.Id, "https://scontent.xx.fbcdn.net/real.mp4", CancellationToken.None);

        var reloaded = await db.Messages.SingleAsync();
        Assert.Null(reloaded.MediaDownloadError);
        Assert.Equal("video/mp4", reloaded.MimeType);
        Assert.NotNull(reloaded.MediaUrl);
    }

    private sealed class FakeDownloadProvider(DownloadedMedia toReturn) : IChannelProvider
    {
        public bool VerifyWebhookToken(string verifyToken) => throw new NotSupportedException();
        public string? ExtractChannelExternalId(System.Text.Json.JsonElement payload) => throw new NotSupportedException();

        public Task<IReadOnlyList<ParsedWebhookMessage>> ParseWebhookAsync(Channel channel, System.Text.Json.JsonElement payload, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ParsedStatusUpdate>> ParseStatusUpdatesAsync(Channel channel, System.Text.Json.JsonElement payload, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<string?> SendMessageAsync(Channel channel, string conversationExternalId, string body, string? messageTag, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<string?> SendTemplateAsync(
            Channel channel, string conversationExternalId, string templateName, string languageCode,
            IReadOnlyList<string> parameters, CancellationToken ct) => throw new NotSupportedException();

        public Task MarkAsReadAsync(Channel channel, string messageExternalId, CancellationToken ct) => throw new NotSupportedException();

        public Task<DownloadedMedia> DownloadMediaAsync(Channel channel, string mediaExternalId, CancellationToken ct) =>
            Task.FromResult(toReturn);

        public Task<string> UploadMediaAsync(Channel channel, Stream content, string mimeType, string fileName, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<string?> SendMediaMessageAsync(
            Channel channel, string conversationExternalId, string mediaExternalId, MessageType type,
            string? caption, bool isVoiceNote, string? messageTag, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<WhatsAppTemplateInfo>> GetApprovedTemplatesAsync(Channel channel, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<ContactProfile> GetContactProfileAsync(Channel channel, string contactExternalId, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class SingleProviderFactory(IChannelProvider provider) : IChannelProviderFactory
    {
        public IChannelProvider GetProvider(ChannelType type) => provider;
    }

    /// <summary>Never called for the html-rejection tests (validated before any ffmpeg step); the real-content-type test is Video with no thumbnail/waveform step either.</summary>
    private sealed class ThrowingMediaProcessor : IMediaProcessor
    {
        public Task TranscodeToOggOpusAsync(string inputPath, string outputPath, CancellationToken ct) => throw new NotSupportedException();
        public Task TranscodeToAacAsync(string inputPath, string outputPath, CancellationToken ct) => throw new NotSupportedException();
        public Task<int?> GetAudioDurationSecondsAsync(string inputPath, CancellationToken ct) => throw new NotSupportedException();
        public Task GenerateImageThumbnailAsync(string inputPath, string outputPath, int maxDimension, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<short>> GenerateWaveformPeaksAsync(string inputPath, int peakCount, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class NoOpInboxEventPublisher : IInboxEventPublisher
    {
        public Task MessageReceivedAsync(Guid channelId, Guid? assignedTo, object payload, CancellationToken ct) => Task.CompletedTask;
        public Task MessageSentAsync(Guid channelId, Guid? assignedTo, object payload, CancellationToken ct) => Task.CompletedTask;
        public Task ConversationAssignedAsync(Guid channelId, Guid? assignedTo, object payload, CancellationToken ct) => Task.CompletedTask;
        public Task ConversationStatusChangedAsync(Guid channelId, Guid? assignedTo, object payload, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Office.Api.Tests";
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
