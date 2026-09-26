using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Office.Api.Auth;
using Office.Api.Channels;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Features.Conversations;
using Office.Api.Features.CustomerChats;
using Office.Api.Realtime;

namespace Office.Api.Tests.Features.CustomerChats;

/// <summary>
/// A мизоҷ sending a photo, video, PDF or a recorded voice note into a chat — called as мизоҷ A
/// through A's real tenant filter. B's chat and the company's are not found (nothing stored,
/// nothing sent); the plan, the account and the 24-hour window are checked as for a text reply;
/// only types Instagram delivers (and a browser would never run) are taken; the stored file gets
/// a new name.
/// </summary>
public class CustomerChatMediaTests : IDisposable
{
    private sealed class FixedTenant(TenantIdentity identity) : ITenantContext
    {
        public TenantIdentity Current => identity;
    }

    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Office.Api";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Test";
    }

    private sealed class RecordingJobs : IBackgroundJobClient
    {
        public List<(string Method, object?[] Args)> Created { get; } = [];

        public string Create(Job job, IState state)
        {
            Created.Add((job.Method.Name, job.Args.ToArray()));
            return "job-1";
        }

        public bool ChangeState(string jobId, IState state, string? expectedState) => true;
    }

    private sealed class RecordingEvents : IInboxEventPublisher
    {
        public int Sent { get; private set; }
        public Task MessageReceivedAsync(Guid channelId, Guid? assignedTo, object payload, CancellationToken ct) => Task.CompletedTask;
        public Task MessageSentAsync(Guid channelId, Guid? assignedTo, object payload, CancellationToken ct) { Sent++; return Task.CompletedTask; }
        public Task ConversationAssignedAsync(Guid channelId, Guid? assignedTo, object payload, CancellationToken ct) => Task.CompletedTask;
        public Task ConversationStatusChangedAsync(Guid channelId, Guid? assignedTo, object payload, CancellationToken ct) => Task.CompletedTask;
    }

    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly string _root = Directory.CreateTempSubdirectory("chat-media-tests-").FullName;
    private readonly IConfiguration _configuration;
    private readonly Guid _customerA = Guid.NewGuid();
    private readonly Guid _customerB = Guid.NewGuid();
    private readonly Guid _channelA = Guid.NewGuid();
    private readonly Guid _channelB = Guid.NewGuid();
    private readonly Guid _companyChannel = Guid.NewGuid();
    private readonly Guid _a1 = Guid.NewGuid(); // A's, window open
    private readonly Guid _a2 = Guid.NewGuid(); // A's, window closed
    private readonly Guid _b1 = Guid.NewGuid(); // B's
    private readonly Guid _c1 = Guid.NewGuid(); // the company's

    public CustomerChatMediaTests()
    {
        _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Uploads:RootPath"] = _root,
            ["Subscriptions:Plans:0:Tier"] = "Pro",
            ["Subscriptions:Plans:0:MonthlyPrice"] = "200",
            ["Subscriptions:Plans:0:Limits:ActiveAutomations"] = "10",
        }).Build();

        using var db = Open(null);
        db.Customers.AddRange(
            new Customer { Id = _customerA, Email = "a@example.com", FullName = "Дӯкони A", TrialEndsAt = DateTimeOffset.UtcNow.AddDays(5) },
            new Customer { Id = _customerB, Email = "b@example.com", FullName = "B", TrialEndsAt = DateTimeOffset.UtcNow.AddDays(5) });
        db.Channels.AddRange(Channel(_channelA, _customerA), Channel(_channelB, _customerB), Channel(_companyChannel, null));
        db.Conversations.AddRange(
            Chat(_a1, _channelA, open: true), Chat(_a2, _channelA, open: false), Chat(_b1, _channelB, open: true), Chat(_c1, _companyChannel, open: true));
        db.SaveChanges();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static Channel Channel(Guid id, Guid? owner) =>
        new() { Id = id, Type = ChannelType.Instagram, Name = "ig", ExternalId = id.ToString(), CustomerId = owner, IsActive = true };

    private static Conversation Chat(Guid id, Guid channelId, bool open) => new()
    {
        Id = id, ChannelId = channelId, ExternalId = id.ToString()[..8], CreatedAt = DateTimeOffset.UtcNow,
        WindowExpiresAt = DateTimeOffset.UtcNow.AddHours(open ? 5 : -1),
    };

    private AppDbContext Open(TenantIdentity? tenant) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_databaseName).Options,
        tenant is null ? null : new FixedTenant(tenant.Value));

    private ClaimsPrincipal PrincipalA => new(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, _customerA.ToString())], "test"));

    private static IFormFile File(string contentType, string name, long? declaredLength = null)
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        return new FormFile(new MemoryStream(bytes), 0, declaredLength ?? bytes.Length, "file", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }

    private async Task<(IResult Result, RecordingJobs Jobs, RecordingEvents Events)> Send(Guid chatId, IFormFile file)
    {
        var jobs = new RecordingJobs();
        var events = new RecordingEvents();
        await using var db = Open(new TenantIdentity(TenantScope.Customer, _customerA));
        var result = await CustomerChatsEndpoints.SendMediaAsync(
            chatId, file, PrincipalA, db, jobs, _configuration, new TestEnvironment(_root), events, CancellationToken.None);
        return (result, jobs, events);
    }

    private async Task<(IResult Result, RecordingJobs Jobs, RecordingEvents Events)> SendVoice(Guid chatId, IFormFile file)
    {
        var jobs = new RecordingJobs();
        var events = new RecordingEvents();
        await using var db = Open(new TenantIdentity(TenantScope.Customer, _customerA));
        var result = await CustomerChatsEndpoints.SendVoiceNoteAsync(
            chatId, file, PrincipalA, db, jobs, _configuration, new TestEnvironment(_root), events, CancellationToken.None);
        return (result, jobs, events);
    }

    private static int? Status(IResult result) => (result as IStatusCodeHttpResult)?.StatusCode;

    private int StoredFiles() => Directory.Exists(Path.Combine(_root, "whatsapp-media"))
        ? Directory.GetFiles(Path.Combine(_root, "whatsapp-media"), "*", SearchOption.AllDirectories).Length
        : 0;

    private int Messages()
    {
        using var db = Open(null);
        return db.Messages.Count();
    }

    [Fact]
    public async Task APhoto_IsStoredUnderANewName_AndQueuedForSending()
    {
        var (result, jobs, events) = await Send(_a1, File("image/jpeg", "../../../etc/passwd.jpg"));

        var dto = Assert.IsType<Accepted<MessageDto>>(result).Value!;
        await using var db = Open(null);
        var message = await db.Messages.SingleAsync();
        Assert.Equal((MessageType.Image, "image/jpeg", "Дӯкони A", MessageDeliveryStatus.Pending),
            (message.Type, message.MimeType, message.SentByUserName, message.DeliveryStatus));
        Assert.Equal("passwd.jpg", message.OriginalFileName); // the name only, never the path
        Assert.StartsWith(Path.Combine("whatsapp-media", _channelA.ToString()), message.MediaUrl);
        Assert.Matches(@"^[0-9a-f-]{36}\.jpg$", Path.GetFileName(message.MediaUrl)!);
        Assert.True(System.IO.File.Exists(Path.Combine(_root, message.MediaUrl!)));
        var (method, args) = Assert.Single(jobs.Created);
        Assert.Equal((nameof(MediaSendJob.SendAsync), message.Id, false), (method, (Guid)args[0]!, (bool)args[1]!));
        Assert.Equal(1, events.Sent);
        Assert.Equal(message.Id, dto.Id);
    }

    [Fact]
    public async Task AWebp_IsTaken_TheJobMakesItAJpeg()
    {
        var (result, _, _) = await Send(_a1, File("image/webp", "photo.webp"));

        Assert.IsType<Accepted<MessageDto>>(result);
        await using var db = Open(null);
        Assert.EndsWith(".webp", (await db.Messages.SingleAsync()).MediaUrl);
    }

    [Fact]
    public async Task AnotherMizojsChat_OrTheCompanys_IsNotFound_AndNothingIsStoredOrSent()
    {
        foreach (var chat in new[] { _b1, _c1 })
        {
            var (result, jobs, events) = await Send(chat, File("image/jpeg", "a.jpg"));
            Assert.Equal(404, Status(result));
            Assert.Empty(jobs.Created);
            Assert.Equal(0, events.Sent);
        }
        Assert.Equal(0, Messages());
        Assert.Equal(0, StoredFiles());
    }

    [Fact]
    public async Task WithoutAPlan_IsForbidden_AndNothingIsStored()
    {
        await using (var db = Open(null))
        {
            (await db.Customers.SingleAsync(c => c.Id == _customerA)).TrialEndsAt = DateTimeOffset.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
        }

        Assert.Equal(403, Status((await Send(_a1, File("image/jpeg", "a.jpg"))).Result));
        Assert.Equal(0, Messages());
        Assert.Equal(0, StoredFiles());
    }

    [Fact]
    public async Task AClosedWindow_OrADisconnectedAccount_IsAConflict()
    {
        Assert.Equal(409, Status((await Send(_a2, File("image/jpeg", "a.jpg"))).Result));

        await using (var db = Open(null))
        {
            (await db.Channels.SingleAsync(c => c.Id == _channelA)).RequiresReconnect = true;
            await db.SaveChangesAsync();
        }
        Assert.Equal(409, Status((await Send(_a1, File("image/jpeg", "a.jpg"))).Result));
        Assert.Equal(0, StoredFiles());
    }

    [Theory]
    [InlineData("image/svg+xml", "x.svg")]
    [InlineData("text/html", "x.html")]
    [InlineData("application/x-msdownload", "x.exe")]
    [InlineData("audio/webm", "x.webm")]
    [InlineData("", "x.jpg")]
    public async Task ATypeInstagramWontDeliver_OrABrowserWouldRun_IsRefused(string contentType, string name)
    {
        Assert.Equal(400, Status((await Send(_a1, File(contentType, name))).Result));
        Assert.Equal(0, Messages());
        Assert.Equal(0, StoredFiles());
    }

    [Fact]
    public async Task AnImageOver8MB_IsRefused()
    {
        Assert.Equal(400, Status((await Send(_a1, File("image/png", "big.png", declaredLength: 8 * 1024 * 1024 + 1))).Result));
        Assert.Equal(0, StoredFiles());
    }

    [Fact]
    public void TheAllowedTypes_AreOnlyWhatInstagramDelivers()
    {
        Assert.DoesNotContain(CustomerChatsEndpoints.SendableMediaTypes.Keys, k => k.Contains("svg") || k.Contains("html") || k.StartsWith("text/"));
        Assert.All(CustomerChatsEndpoints.SendableMediaTypes.Values, ext => Assert.Matches(@"^\.[a-z0-9]{3,4}$", ext));
    }

    // ── Voice notes (recorded in the browser) ──

    [Theory]
    [InlineData("audio/webm;codecs=opus", ".webm", "audio/webm")] // Chrome, Edge
    [InlineData("audio/ogg;codecs=opus", ".ogg", "audio/ogg")] // Firefox
    [InlineData("audio/mp4", ".mp4", "audio/mp4")] // Safari (iPhone) — ".mp4", so the job still makes it clean AAC
    public async Task AVoiceNote_IsStoredUnderANewName_AndQueuedAsAVoiceNote(string contentType, string extension, string storedType)
    {
        var (result, jobs, events) = await SendVoice(_a1, File(contentType, "../../x" + extension));

        var dto = Assert.IsType<Accepted<MessageDto>>(result).Value!;
        await using var db = Open(null);
        var message = await db.Messages.SingleAsync();
        Assert.Equal((MessageType.Audio, storedType, "Дӯкони A", MessageDeliveryStatus.Pending, (string?)null),
            (message.Type, message.MimeType, message.SentByUserName, message.DeliveryStatus, message.OriginalFileName));
        Assert.StartsWith(Path.Combine("whatsapp-media", _channelA.ToString()), message.MediaUrl);
        Assert.Matches(@"^[0-9a-f-]{36}\" + extension + "$", Path.GetFileName(message.MediaUrl)!);
        Assert.True(System.IO.File.Exists(Path.Combine(_root, message.MediaUrl!)));
        var (method, args) = Assert.Single(jobs.Created);
        Assert.Equal((nameof(MediaSendJob.SendAsync), message.Id, true), (method, (Guid)args[0]!, (bool)args[1]!));
        Assert.Equal(1, events.Sent);
        Assert.Equal(message.Id, dto.Id);
    }

    [Fact]
    public async Task AVoiceNote_ToAnotherMizojsChat_OrTheCompanys_IsNotFound_AndNothingIsStoredOrSent()
    {
        foreach (var chat in new[] { _b1, _c1 })
        {
            var (result, jobs, events) = await SendVoice(chat, File("audio/webm", "v.webm"));
            Assert.Equal(404, Status(result));
            Assert.Empty(jobs.Created);
            Assert.Equal(0, events.Sent);
        }
        Assert.Equal(0, Messages());
        Assert.Equal(0, StoredFiles());
    }

    [Fact]
    public async Task AVoiceNote_WithoutAPlan_IsForbidden_AndNothingIsStored()
    {
        await using (var db = Open(null))
        {
            (await db.Customers.SingleAsync(c => c.Id == _customerA)).TrialEndsAt = DateTimeOffset.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
        }

        Assert.Equal(403, Status((await SendVoice(_a1, File("audio/webm", "v.webm"))).Result));
        Assert.Equal(0, Messages());
        Assert.Equal(0, StoredFiles());
    }

    [Fact]
    public async Task AVoiceNote_AfterTheWindow_OrToADisconnectedAccount_IsAConflict()
    {
        Assert.Equal(409, Status((await SendVoice(_a2, File("audio/webm", "v.webm"))).Result));

        await using (var db = Open(null))
        {
            (await db.Channels.SingleAsync(c => c.Id == _channelA)).RequiresReconnect = true;
            await db.SaveChangesAsync();
        }
        Assert.Equal(409, Status((await SendVoice(_a1, File("audio/webm", "v.webm"))).Result));
        Assert.Equal(0, StoredFiles());
    }

    [Theory]
    [InlineData("audio/mpeg", "x.mp3")] // a music file goes by the paperclip, not as a voice note
    [InlineData("image/jpeg", "x.jpg")]
    [InlineData("text/html", "x.html")]
    [InlineData("image/svg+xml", "x.svg")]
    [InlineData("", "x.webm")]
    public async Task NotARecording_IsRefused(string contentType, string name)
    {
        Assert.Equal(400, Status((await SendVoice(_a1, File(contentType, name))).Result));
        Assert.Equal(0, Messages());
        Assert.Equal(0, StoredFiles());
    }

    [Fact]
    public async Task AnEmptyRecording_OrOneOverTheLimit_IsRefused()
    {
        var empty = Assert.IsType<ProblemHttpResult>((await SendVoice(_a1, File("audio/webm", "v.webm", declaredLength: 0))).Result);
        Assert.Equal((400, "Паёми овозӣ фиристода нашуд"), (empty.StatusCode, empty.ProblemDetails.Title)); // "not recorded", never "too long"
        var tooLong = Assert.IsType<ProblemHttpResult>((await SendVoice(_a1, File("audio/webm", "v.webm", declaredLength: 25L * 1024 * 1024 + 1))).Result);
        Assert.Equal((400, "Паёми овозӣ дароз аст"), (tooLong.StatusCode, tooLong.ProblemDetails.Title));
        Assert.Equal(0, StoredFiles());
    }

    [Fact]
    public void VoiceNoteTypes_AreOnlyRecordings_NeverWhatOnlyTheTranscodeWrites()
    {
        Assert.All(CustomerChatsEndpoints.VoiceNoteTypes.Keys, k => Assert.StartsWith("audio/", k));
        Assert.DoesNotContain(".m4a", CustomerChatsEndpoints.VoiceNoteTypes.Values); // else a Safari file would skip ffmpeg
        Assert.All(CustomerChatsEndpoints.VoiceNoteTypes.Values, ext => Assert.Matches(@"^\.[a-z0-9]{3,4}$", ext));
    }
}
