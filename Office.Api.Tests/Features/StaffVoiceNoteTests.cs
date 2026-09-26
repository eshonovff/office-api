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
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Features.Conversations;
using Office.Api.Realtime;

namespace Office.Api.Tests.Features;

/// <summary>
/// A staff voice note (POST /api/conversations/{id}/voice-note): only a browser recording is taken
/// — Chrome/Edge WebM, Firefox Ogg, Safari/iPhone MP4 — stored under a new name with an extension
/// the transcode never writes, and queued as a voice note; a chat the person may not open is not
/// found, a chat taken by a colleague is read-only.
/// </summary>
public sealed class StaffVoiceNoteTests : IDisposable
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

    private sealed class Guard(params Guid[] allowed) : IChannelAccessGuard
    {
        public Task<bool> HasAccessAsync(ClaimsPrincipal principal, Guid channelId, Guid? assignedTo, CancellationToken ct) =>
            Task.FromResult(allowed.Contains(channelId));
        public Task<IQueryable<Conversation>> ApplyAccessFilterAsync(IQueryable<Conversation> query, ClaimsPrincipal principal, CancellationToken ct) => throw new NotSupportedException();
        public Task<(IQueryable<Channel> Query, bool Joinable)> ApplyChannelAccessFilterAsync(IQueryable<Channel> query, ClaimsPrincipal principal, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> CanAccessChannelAsync(ClaimsPrincipal principal, Guid channelId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> CanUserBeAssignedToChannelAsync(Guid userId, Guid channelId, CancellationToken ct) => throw new NotSupportedException();
        public IQueryable<User> ApplyAssignableUsersFilter(IQueryable<User> query, Guid channelId) => throw new NotSupportedException();
    }

    private sealed class RecordingJobs : IBackgroundJobClient
    {
        public List<(string Method, object?[] Args)> Created { get; } = [];
        public string Create(Job job, IState state) { Created.Add((job.Method.Name, job.Args.ToArray())); return "job"; }
        public bool ChangeState(string jobId, IState state, string? expectedState) => true;
    }

    private sealed class NoEvents : IInboxEventPublisher
    {
        public Task MessageReceivedAsync(Guid channelId, Guid? assignedTo, object payload, CancellationToken ct) => Task.CompletedTask;
        public Task MessageSentAsync(Guid channelId, Guid? assignedTo, object payload, CancellationToken ct) => Task.CompletedTask;
        public Task ConversationAssignedAsync(Guid channelId, Guid? assignedTo, object payload, CancellationToken ct) => Task.CompletedTask;
        public Task ConversationStatusChangedAsync(Guid channelId, Guid? assignedTo, object payload, CancellationToken ct) => Task.CompletedTask;
    }

    private readonly string _name = Guid.NewGuid().ToString();
    private readonly string _root = Directory.CreateTempSubdirectory("staff-voice-").FullName;
    private readonly IConfiguration _configuration;
    private readonly Guid _me = Guid.NewGuid();
    private readonly Guid _colleague = Guid.NewGuid();
    private readonly Guid _channel = Guid.NewGuid();
    private readonly Guid _free = Guid.NewGuid();
    private readonly Guid _taken = Guid.NewGuid();

    public StaffVoiceNoteTests()
    {
        _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Uploads:RootPath"] = _root }).Build();
        using var db = Open();
        db.Users.AddRange(
            new User { Id = _me, FullName = "Фаридун", Username = "me", PasswordHash = "x" },
            new User { Id = _colleague, FullName = "Ҳамкор", Username = "colleague", PasswordHash = "x" });
        db.Channels.Add(new Channel { Id = _channel, Type = ChannelType.Instagram, Name = "ig", ExternalId = "ig", IsActive = true });
        db.Conversations.AddRange(
            new Conversation { Id = _free, ChannelId = _channel, ExternalId = "fan-1", CreatedAt = DateTimeOffset.UtcNow, WindowExpiresAt = DateTimeOffset.UtcNow.AddHours(5) },
            new Conversation { Id = _taken, ChannelId = _channel, ExternalId = "fan-2", AssignedTo = _colleague, CreatedAt = DateTimeOffset.UtcNow });
        db.SaveChanges();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private AppDbContext Open() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_name).Options, new FixedTenant(new TenantIdentity(TenantScope.Staff, null)));

    private ClaimsPrincipal Me => new(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, _me.ToString())], "test"));

    private static IFormFile Recording(string contentType, string name, long? length = null)
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        return new FormFile(new MemoryStream(bytes), 0, length ?? bytes.Length, "file", name) { Headers = new HeaderDictionary(), ContentType = contentType };
    }

    private async Task<(IResult Result, RecordingJobs Jobs)> Send(Guid chat, IFormFile file, params Guid[] allowed)
    {
        var jobs = new RecordingJobs();
        await using var db = Open();
        var result = await ConversationsEndpoints.UploadVoiceNoteAsync(
            chat, file, Me, db, new Guard(allowed.Length == 0 ? [_channel] : allowed), jobs, _configuration, new TestEnvironment(_root), new NoEvents(), CancellationToken.None);
        return (result, jobs);
    }

    private static int? Status(IResult result) => (result as IStatusCodeHttpResult)?.StatusCode;

    private int Stored() => Directory.Exists(Path.Combine(_root, "whatsapp-media"))
        ? Directory.GetFiles(Path.Combine(_root, "whatsapp-media"), "*", SearchOption.AllDirectories).Length : 0;

    [Theory]
    [InlineData("audio/webm;codecs=opus", ".webm", "audio/webm")] // Chrome, Edge
    [InlineData("audio/ogg;codecs=opus", ".opus", "audio/ogg")] // Firefox
    [InlineData("audio/mp4", ".mp4", "audio/mp4")] // Safari, iPhone
    public async Task ARecording_IsStoredUnderANewName_AndQueuedAsAVoiceNote(string contentType, string extension, string storedType)
    {
        var (result, jobs) = await Send(_free, Recording(contentType, "voice-note-1" + extension));

        Assert.IsType<Accepted<MessageDto>>(result);
        await using var db = Open();
        var message = await db.Messages.SingleAsync();
        Assert.Equal((MessageType.Audio, storedType, "Фаридун"), (message.Type, message.MimeType, message.SentByUserName));
        Assert.Matches(@"^[0-9a-f-]{36}\" + extension + "$", Path.GetFileName(message.MediaUrl)!);
        Assert.True(File.Exists(Path.Combine(_root, message.MediaUrl!)));
        var (method, args) = Assert.Single(jobs.Created);
        Assert.Equal(("SendAsync", message.Id, true), (method, (Guid)args[0]!, (bool)args[1]!));
    }

    [Theory]
    [InlineData("text/html", "x.html")]
    [InlineData("image/svg+xml", "x.svg")]
    [InlineData("audio/mpeg", "song.mp3")] // a music file goes by the paperclip
    [InlineData("", "voice.webm")]
    public async Task NotARecording_IsRefused(string contentType, string name)
    {
        Assert.Equal(400, Status((await Send(_free, Recording(contentType, name))).Result));
        Assert.Equal(0, Stored());
    }

    [Fact]
    public async Task AnEmptyRecording_IsRefused()
    {
        Assert.Equal(400, Status((await Send(_free, Recording("audio/webm", "v.webm", length: 0))).Result));
    }

    [Fact]
    public async Task AChatThePersonMayNotOpen_IsNotFound()
    {
        Assert.Equal(404, Status((await Send(_free, Recording("audio/webm", "v.webm"), Guid.NewGuid())).Result));
        Assert.Equal(0, Stored());
    }

    [Fact]
    public async Task AChatTakenByAColleague_IsReadOnly()
    {
        Assert.Equal(403, Status((await Send(_taken, Recording("audio/webm", "v.webm"))).Result));
        Assert.Equal(0, Stored());
    }
}
