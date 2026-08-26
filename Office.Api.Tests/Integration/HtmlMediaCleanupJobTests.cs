using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Office.Api.Channels;
using Office.Api.Data;
using Office.Api.Data.Entities;

namespace Office.Api.Tests.Integration;

/// <summary>Real disk I/O against a temp directory (not just EF InMemory) — the bug this cleans up only exists as actual bytes on disk.</summary>
public class HtmlMediaCleanupJobTests : IDisposable
{
    private readonly string _rootPath = Directory.CreateTempSubdirectory("office-api-tests-").FullName;

    public void Dispose() => Directory.Delete(_rootPath, recursive: true);

    private AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private HtmlMediaCleanupJob MakeJob(AppDbContext db)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Uploads:RootPath"] = _rootPath })
            .Build();
        return new HtmlMediaCleanupJob(db, config, new FakeWebHostEnvironment(), NullLogger<HtmlMediaCleanupJob>.Instance);
    }

    private Message SeedMessage(AppDbContext db, string relativeMediaPath, byte[] fileContent)
    {
        var channel = new Channel
        {
            Id = Guid.CreateVersion7(),
            Type = ChannelType.Instagram,
            Name = "Test channel",
            ExternalId = "17841400000000000",
            CredentialsEncrypted = "irrelevant",
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
            Type = MessageType.Image,
            DeliveryStatus = MessageDeliveryStatus.Delivered,
            MediaUrl = relativeMediaPath,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Channels.Add(channel);
        db.Conversations.Add(conversation);
        db.Messages.Add(message);
        db.SaveChanges();

        var fullPath = Path.Combine(_rootPath, relativeMediaPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, fileContent);

        return message;
    }

    [Fact]
    public async Task RunAsync_FileIsActuallyHtml_MarksMediaDownloadErrorAndDeletesTheFile()
    {
        var db = CreateDb();
        var htmlBytes = Encoding.UTF8.GetBytes("<!doctype html><html><body>expired</body></html>");
        var message = SeedMessage(db, "whatsapp-media/c1/garbage.jpg", htmlBytes);
        var fullPath = Path.Combine(_rootPath, message.MediaUrl!);

        await MakeJob(db).RunAsync(CancellationToken.None);

        var reloaded = await db.Messages.SingleAsync();
        Assert.NotNull(reloaded.MediaDownloadError);
        Assert.Null(reloaded.MediaUrl);
        Assert.False(File.Exists(fullPath));
    }

    [Fact]
    public async Task RunAsync_RealMediaFile_IsLeftUntouched()
    {
        var db = CreateDb();
        byte[] jpegLikeBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];
        var message = SeedMessage(db, "whatsapp-media/c1/real.jpg", jpegLikeBytes);
        var fullPath = Path.Combine(_rootPath, message.MediaUrl!);

        await MakeJob(db).RunAsync(CancellationToken.None);

        var reloaded = await db.Messages.SingleAsync();
        Assert.Null(reloaded.MediaDownloadError);
        Assert.Equal("whatsapp-media/c1/real.jpg", reloaded.MediaUrl);
        Assert.True(File.Exists(fullPath));
    }

    [Fact]
    public async Task RunAsync_AlreadyFlaggedMessage_IsSkipped()
    {
        // MediaDownloadError already set (e.g. by MediaDownloadJob itself, post-fix) — not this
        // job's job to touch it again, and there's no file to have been mistakenly saved anyway.
        var db = CreateDb();
        var message = SeedMessage(db, "whatsapp-media/c1/whatever.jpg", "<html></html>"u8.ToArray());
        message.MediaDownloadError = "already flagged elsewhere";
        await db.SaveChangesAsync();

        await MakeJob(db).RunAsync(CancellationToken.None);

        var reloaded = await db.Messages.SingleAsync();
        Assert.Equal("already flagged elsewhere", reloaded.MediaDownloadError);
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
