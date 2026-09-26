using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Office.Api.Channels.ContactProfiles;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Media;

namespace Office.Api.Tests.Channels.ContactProfiles;

/// <summary>
/// Our copy of a contact's picture: only Meta's CDN over https is ever fetched (a redirect too),
/// at most 5 MB, only a picture; the copy lands in the channel's own folder; the job keeps the
/// newest and drops the old one — and keeps nothing for a contact deleted meanwhile.
/// </summary>
public sealed class ContactAvatarDownloaderTests : IDisposable
{
    private const string Cdn = "https://scontent-fra3-1.cdninstagram.com/v/p.jpg?oe=1";
    private readonly string _root = Directory.CreateTempSubdirectory("avatars-").FullName;
    private readonly Guid _channel = Guid.NewGuid();
    private readonly Guid _chat = Guid.NewGuid();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class FakeCdn : HttpMessageHandler
    {
        public Dictionary<string, Func<HttpResponseMessage>> Routes { get; } = [];
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Requested.Add(url);
            return Task.FromResult(Routes.TryGetValue(url, out var respond) ? respond() : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class FakeFfmpeg : IMediaProcessor
    {
        public List<string> Inputs { get; } = [];
        public Exception? Fail { get; set; }

        public Task GenerateImageThumbnailAsync(string inputPath, string outputPath, int maxDimension, CancellationToken ct)
        {
            Inputs.Add(File.ReadAllText(inputPath));
            if (Fail is not null)
                throw Fail;
            File.WriteAllBytes(outputPath, [0xFF, 0xD8, 0xFF, 0xD9]);
            return Task.CompletedTask;
        }

        public Task TranscodeToOggOpusAsync(string inputPath, string outputPath, CancellationToken ct) => throw new NotSupportedException();
        public Task TranscodeToAacAsync(string inputPath, string outputPath, CancellationToken ct) => throw new NotSupportedException();
        public Task ConvertImageToJpegAsync(string inputPath, string outputPath, CancellationToken ct) => throw new NotSupportedException();
        public Task<int?> GetAudioDurationSecondsAsync(string inputPath, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<short>> GenerateWaveformPeaksAsync(string inputPath, int peakCount, CancellationToken ct) => throw new NotSupportedException();
    }

    private static HttpResponseMessage Picture(string body = "a picture", string type = "image/jpeg", long? declaredLength = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(type);
        if (declaredLength is not null)
            response.Content.Headers.ContentLength = declaredLength;
        return response;
    }

    /// <summary>A body whose length nobody knows in advance.</summary>
    private sealed class Unmeasured(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static HttpResponseMessage Redirect(string to)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(to);
        return response;
    }

    private (ContactAvatarDownloader Downloader, FakeCdn Cdn, FakeFfmpeg Ffmpeg) Make()
    {
        var cdn = new FakeCdn();
        var ffmpeg = new FakeFfmpeg();
        return (new ContactAvatarDownloader(new HttpClient(cdn), ffmpeg, NullLogger<ContactAvatarDownloader>.Instance), cdn, ffmpeg);
    }

    [Theory]
    [InlineData("https://scontent.cdninstagram.com/p.jpg", true)]
    [InlineData("https://scontent-fra3-1.xx.fbcdn.net/p.jpg", true)]
    [InlineData("https://platform-lookaside.fbsbx.com/platform/profilepic/?psid=1", true)]
    [InlineData("http://scontent.cdninstagram.com/p.jpg", false)] // not https
    [InlineData("https://cdninstagram.com.evil.example/p.jpg", false)]
    [InlineData("https://evilcdninstagram.com/p.jpg", false)]
    [InlineData("https://user:pass@scontent.cdninstagram.com/p.jpg", false)]
    [InlineData("https://scontent.cdninstagram.com:8443/p.jpg", false)]
    [InlineData("https://169.254.169.254/latest/meta-data", false)]
    [InlineData("https://localhost/p.jpg", false)]
    public void OnlyMetasCdnOverHttps(string url, bool allowed)
    {
        Assert.Equal(allowed, ContactAvatarDownloader.IsMetaCdn(new Uri(url)));
    }

    [Fact]
    public async Task APicture_IsCopiedIntoTheChannelsOwnFolder()
    {
        var (downloader, cdn, ffmpeg) = Make();
        cdn.Routes[Cdn] = () => Picture();

        var saved = await downloader.SaveAsync(Cdn, _channel, _chat, _root, CancellationToken.None);

        Assert.NotNull(saved);
        Assert.StartsWith(Path.Combine("whatsapp-media", _channel.ToString(), "avatars", _chat.ToString()), saved);
        Assert.EndsWith(".jpg", saved);
        Assert.True(File.Exists(Path.Combine(_root, saved)));
        Assert.Equal(["a picture"], ffmpeg.Inputs);
    }

    [Fact]
    public async Task ALinkElsewhere_IsNeverRequested()
    {
        var (downloader, cdn, _) = Make();

        Assert.Null(await downloader.SaveAsync("https://169.254.169.254/latest/meta-data", _channel, _chat, _root, CancellationToken.None));
        Assert.Null(await downloader.SaveAsync("http://scontent.cdninstagram.com/p.jpg", _channel, _chat, _root, CancellationToken.None));
        Assert.Empty(cdn.Requested);
    }

    [Fact]
    public async Task ARedirectAwayFromMeta_IsNotFollowed()
    {
        var (downloader, cdn, _) = Make();
        cdn.Routes[Cdn] = () => Redirect("https://internal.example/secret");

        Assert.Null(await downloader.SaveAsync(Cdn, _channel, _chat, _root, CancellationToken.None));
        Assert.Equal([Cdn], cdn.Requested);
    }

    [Fact]
    public async Task ARedirectWithinMeta_IsFollowed()
    {
        var (downloader, cdn, _) = Make();
        const string next = "https://scontent.xx.fbcdn.net/p2.jpg";
        cdn.Routes[Cdn] = () => Redirect(next);
        cdn.Routes[next] = () => Picture();

        Assert.NotNull(await downloader.SaveAsync(Cdn, _channel, _chat, _root, CancellationToken.None));
        Assert.Equal([Cdn, next], cdn.Requested);
    }

    [Fact]
    public async Task EndlessRedirects_StopAfterThree()
    {
        var (downloader, cdn, _) = Make();
        cdn.Routes[Cdn] = () => Redirect(Cdn);

        Assert.Null(await downloader.SaveAsync(Cdn, _channel, _chat, _root, CancellationToken.None));
        Assert.Equal(4, cdn.Requested.Count);
    }

    [Fact]
    public async Task NotAPicture_OrTooBig_IsNotKept()
    {
        var (downloader, cdn, ffmpeg) = Make();
        cdn.Routes[Cdn] = () => Picture(type: "text/html");
        Assert.Null(await downloader.SaveAsync(Cdn, _channel, _chat, _root, CancellationToken.None));

        cdn.Routes[Cdn] = () => Picture(declaredLength: ContactAvatarDownloader.MaxBytes + 1);
        Assert.Null(await downloader.SaveAsync(Cdn, _channel, _chat, _root, CancellationToken.None));

        cdn.Routes[Cdn] = () => // no length said: the limit holds while reading
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new Unmeasured(new byte[ContactAvatarDownloader.MaxBytes + 1])),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            return response;
        };
        Assert.Null(await downloader.SaveAsync(Cdn, _channel, _chat, _root, CancellationToken.None));

        Assert.Empty(ffmpeg.Inputs);
        Assert.False(Directory.Exists(Path.Combine(_root, "whatsapp-media")));
    }

    [Fact]
    public async Task WhatFfmpegRefuses_IsNotKept()
    {
        var (downloader, cdn, ffmpeg) = Make();
        cdn.Routes[Cdn] = () => Picture("ffconcat version 1.0");
        ffmpeg.Fail = new MediaProcessingException("Format not on whitelist");

        Assert.Null(await downloader.SaveAsync(Cdn, _channel, _chat, _root, CancellationToken.None));
    }

    // ── ContactAvatarJob ──

    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Office.Api";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Test";
    }

    private ContactAvatarJob Job(AppDbContext db, ContactAvatarDownloader downloader) => new(
        db, downloader, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Uploads:RootPath"] = _root }).Build(),
        new TestEnvironment(_root));

    private static AppDbContext Db(string name) => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options);

    [Fact]
    public async Task TheJob_KeepsTheNewCopy_AndDropsTheOld()
    {
        var name = Guid.NewGuid().ToString();
        var old = Path.Combine("whatsapp-media", _channel.ToString(), "avatars", $"{_chat}-1.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(_root, old))!);
        await File.WriteAllBytesAsync(Path.Combine(_root, old), [1]);
        await using (var db = Db(name))
        {
            db.Channels.Add(new Channel { Id = _channel, Type = ChannelType.Instagram, Name = "a", ExternalId = "a", IsActive = true });
            db.Conversations.Add(new Conversation { Id = _chat, ChannelId = _channel, ExternalId = "fan", ContactAvatarUrl = Cdn, ContactAvatarPath = old });
            await db.SaveChangesAsync();
        }
        var (downloader, cdn, _) = Make();
        cdn.Routes[Cdn] = () => Picture();

        await using (var db = Db(name))
            await Job(db, downloader).DownloadAsync(_chat, CancellationToken.None);

        await using var check = Db(name);
        var saved = (await check.Conversations.SingleAsync()).ContactAvatarPath!;
        Assert.NotEqual(old, saved);
        Assert.True(File.Exists(Path.Combine(_root, saved)));
        Assert.False(File.Exists(Path.Combine(_root, old)));
    }

    [Fact]
    public async Task TheJob_ForAContactDeletedMeanwhile_KeepsNothing()
    {
        var name = Guid.NewGuid().ToString();
        await using (var db = Db(name))
        {
            db.Channels.Add(new Channel { Id = _channel, Type = ChannelType.Instagram, Name = "a", ExternalId = "a", IsActive = true });
            db.Conversations.Add(new Conversation { Id = _chat, ChannelId = _channel, ExternalId = "fan", ContactAvatarUrl = Cdn });
            await db.SaveChangesAsync();
        }
        var (downloader, cdn, _) = Make();
        cdn.Routes[Cdn] = () =>
        {
            using var other = Db(name); // the мизоҷ deletes the contact while the picture downloads
            other.Conversations.Remove(other.Conversations.Single());
            other.SaveChanges();
            return Picture();
        };

        await using (var db = Db(name))
            await Job(db, downloader).DownloadAsync(_chat, CancellationToken.None);

        var folder = Path.Combine(_root, "whatsapp-media", _channel.ToString(), "avatars");
        Assert.True(!Directory.Exists(folder) || Directory.GetFiles(folder).Length == 0);
    }
}
