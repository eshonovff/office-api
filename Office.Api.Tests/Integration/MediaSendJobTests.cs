using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Office.Api.Channels;
using Office.Api.Channels.WhatsApp;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Media;
using Office.Api.Realtime;

namespace Office.Api.Tests.Integration;

/// <summary>
/// The regression this closes: a voice note whose Meta upload/send step failed AFTER a
/// successful transcode (e.g. a transient Meta 500) got retried by Hangfire from scratch —
/// TranscodeVoiceNoteAsync ran again against a .webm it had already deleted on the first
/// attempt, failing forever with "No such file or directory". Plus: every non-window-closed
/// failure used to leave the message silently "Pending" forever, with no visible reason.
/// </summary>
public class MediaSendJobTests
{
    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private readonly string _rootPath = Directory.CreateTempSubdirectory("office-api-tests-").FullName;

    private (AppDbContext Db, Message Message) SeedVoiceNote(string mimeType = "audio/webm;codecs=opus")
    {
        var db = CreateDb();
        var channel = new Channel
        {
            Id = Guid.CreateVersion7(),
            Type = ChannelType.WhatsApp,
            Name = "Test channel",
            ExternalId = "1206432455895142",
            CredentialsEncrypted = "irrelevant",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var conversation = new Conversation
        {
            Id = Guid.CreateVersion7(),
            ChannelId = channel.Id,
            Channel = channel,
            ExternalId = "992000000000",
            Status = ConversationStatus.InProgress,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var relativePath = $"whatsapp-media/{channel.Id}/voice.webm";
        var message = new Message
        {
            Id = Guid.CreateVersion7(),
            ConversationId = conversation.Id,
            Conversation = conversation,
            Direction = MessageDirection.Outbound,
            Type = MessageType.Audio,
            DeliveryStatus = MessageDeliveryStatus.Pending,
            MediaUrl = relativePath,
            MimeType = mimeType,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Channels.Add(channel);
        db.Conversations.Add(conversation);
        db.Messages.Add(message);
        db.SaveChanges();

        var fullPath = Path.Combine(_rootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, "fake-webm-bytes"u8.ToArray());

        return (db, message);
    }

    private MediaSendJob MakeJob(AppDbContext db, IChannelProvider provider, IMediaProcessor? mediaProcessor = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Uploads:RootPath"] = _rootPath })
            .Build();
        return new MediaSendJob(
            db, new SingleProviderFactory(provider), mediaProcessor ?? new FakeMediaProcessor(),
            config, new FakeWebHostEnvironment(), new NoOpInboxEventPublisher(), NullLogger<MediaSendJob>.Instance);
    }

    [Fact]
    public async Task SendAsync_UploadFailsAfterTranscode_MarksFailedWithReasonAndRethrows()
    {
        var (db, message) = SeedVoiceNote();
        var provider = new FakeProvider { UploadException = new InvalidOperationException("WhatsApp Graph API хатогӣ: rate limited") };
        var job = MakeJob(db, provider);

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.SendAsync(message.Id, isVoiceNote: true, CancellationToken.None));

        var reloaded = await db.Messages.SingleAsync();
        Assert.Equal(MessageDeliveryStatus.Failed, reloaded.DeliveryStatus);
        Assert.Contains("rate limited", reloaded.FailureReason);
        // The whole point: the transcode step's result (MediaUrl -> .ogg) must survive even
        // though the overall attempt failed downstream, or a retry re-runs it against a deleted file.
        Assert.EndsWith(".ogg", reloaded.MediaUrl);
    }

    [Fact]
    public async Task SendAsync_RetryAfterAPartialFailure_DoesNotReTranscodeTheAlreadyDeletedSource()
    {
        var (db, message) = SeedVoiceNote();
        var failingProvider = new FakeProvider { UploadException = new InvalidOperationException("transient") };
        var transcodeCounter = new CountingMediaProcessor();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => MakeJob(db, failingProvider, transcodeCounter).SendAsync(message.Id, isVoiceNote: true, CancellationToken.None));
        Assert.Equal(1, transcodeCounter.TranscodeCallCount);

        // Simulates Hangfire's automatic retry: same message, fresh job instance/DbContext state
        // (message was reloaded from db, which now has MediaUrl already pointed at .ogg).
        var succeedingProvider = new FakeProvider();
        await MakeJob(db, succeedingProvider, transcodeCounter).SendAsync(message.Id, isVoiceNote: true, CancellationToken.None);

        Assert.Equal(1, transcodeCounter.TranscodeCallCount); // still 1 — retry did not re-transcode
        var reloaded = await db.Messages.SingleAsync();
        Assert.Equal(MessageDeliveryStatus.Sent, reloaded.DeliveryStatus);
        Assert.Null(reloaded.FailureReason); // cleared on the successful retry
    }

    [Fact]
    public async Task SendAsync_WhatsAppWindowClosed_MarksFailedWithReasonAndDoesNotRethrow()
    {
        var (db, message) = SeedVoiceNote(mimeType: "audio/ogg"); // already transcoded, isolates this test to the send step
        message.MediaUrl = Path.ChangeExtension(message.MediaUrl, ".ogg");
        File.WriteAllBytes(Path.Combine(_rootPath, message.MediaUrl!), "fake-ogg-bytes"u8.ToArray());
        await db.SaveChangesAsync();
        var provider = new FakeProvider { SendException = new WhatsAppWindowClosedException("window closed") };

        var exception = await Record.ExceptionAsync(() => MakeJob(db, provider).SendAsync(message.Id, isVoiceNote: true, CancellationToken.None));

        Assert.Null(exception); // terminal — Hangfire must not retry, the window won't reopen
        var reloaded = await db.Messages.SingleAsync();
        Assert.Equal(MessageDeliveryStatus.Failed, reloaded.DeliveryStatus);
        Assert.NotNull(reloaded.FailureReason);
    }

    private sealed class FakeProvider : IChannelProvider
    {
        public Exception? UploadException;
        public Exception? SendException;

        public bool VerifyWebhookToken(string verifyToken) => throw new NotSupportedException();
        public string? ExtractChannelExternalId(System.Text.Json.JsonElement payload) => throw new NotSupportedException();
        public Task<IReadOnlyList<ParsedWebhookMessage>> ParseWebhookAsync(Channel channel, System.Text.Json.JsonElement payload, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ParsedStatusUpdate>> ParseStatusUpdatesAsync(Channel channel, System.Text.Json.JsonElement payload, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> SendMessageAsync(Channel channel, string conversationExternalId, string body, string? messageTag, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> SendTemplateAsync(Channel channel, string conversationExternalId, string templateName, string languageCode, IReadOnlyList<string> parameters, CancellationToken ct) => throw new NotSupportedException();
        public Task MarkAsReadAsync(Channel channel, string messageExternalId, CancellationToken ct) => throw new NotSupportedException();
        public Task<DownloadedMedia> DownloadMediaAsync(Channel channel, string mediaExternalId, CancellationToken ct) => throw new NotSupportedException();
        public Task<ContactProfile> GetContactProfileAsync(Channel channel, string contactExternalId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<WhatsAppTemplateInfo>> GetApprovedTemplatesAsync(Channel channel, CancellationToken ct) => throw new NotSupportedException();

        public Task<string> UploadMediaAsync(Channel channel, Stream content, string mimeType, string fileName, CancellationToken ct) =>
            UploadException is not null ? throw UploadException : Task.FromResult("attachment-id-1");

        public Task<string?> SendMediaMessageAsync(
            Channel channel, string conversationExternalId, string mediaExternalId, MessageType type,
            string? caption, bool isVoiceNote, string? messageTag, CancellationToken ct) =>
            SendException is not null ? throw SendException : Task.FromResult<string?>("wamid-1");
    }

    private sealed class SingleProviderFactory(IChannelProvider provider) : IChannelProviderFactory
    {
        public IChannelProvider GetProvider(ChannelType type) => provider;
    }

    private class FakeMediaProcessor : IMediaProcessor
    {
        public virtual Task TranscodeToOggOpusAsync(string inputPath, string outputPath, CancellationToken ct)
        {
            File.WriteAllBytes(outputPath, "fake-ogg-bytes"u8.ToArray());
            return Task.CompletedTask;
        }

        public Task<int?> GetAudioDurationSecondsAsync(string inputPath, CancellationToken ct) => Task.FromResult<int?>(5);
        public Task GenerateImageThumbnailAsync(string inputPath, string outputPath, int maxDimension, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<short>> GenerateWaveformPeaksAsync(string inputPath, int peakCount, CancellationToken ct) => Task.FromResult<IReadOnlyList<short>>([]);
    }

    private sealed class CountingMediaProcessor : FakeMediaProcessor
    {
        public int TranscodeCallCount { get; private set; }

        public override Task TranscodeToOggOpusAsync(string inputPath, string outputPath, CancellationToken ct)
        {
            TranscodeCallCount++;
            return base.TranscodeToOggOpusAsync(inputPath, outputPath, ct);
        }
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
