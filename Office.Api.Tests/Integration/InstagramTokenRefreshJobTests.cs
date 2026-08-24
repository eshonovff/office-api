using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Office.Api.Channels;
using Office.Api.Channels.Instagram;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Realtime;

namespace Office.Api.Tests.Integration;

/// <summary>
/// Real-world trigger for this whole job: ig_exchange_token gives a ~60-day token and nothing
/// ever refreshed it — a channel would silently stop working the day it expired. These tests
/// exercise the actual RunAsync against an EF InMemory db and a fake HttpMessageHandler (no
/// real network), same spirit as MediaDownloadJobContentTypeTests.
/// </summary>
public class InstagramTokenRefreshJobTests
{
    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Channel SeedChannel(AppDbContext db, DateTimeOffset? expiresAt, string token = "old-token")
    {
        var channel = new Channel
        {
            Id = Guid.CreateVersion7(),
            Type = ChannelType.Instagram,
            Name = "Test IG channel",
            ExternalId = "17841400000000000",
            CredentialsEncrypted = $$"""{"instagramAccountId":"17841400000000000","accessToken":"{{token}}"}""",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            CredentialsExpiresAt = expiresAt,
        };
        db.Channels.Add(channel);
        db.SaveChanges();
        return channel;
    }

    private static InstagramTokenRefreshJob MakeJob(AppDbContext db, FakeHttpMessageHandler handler) =>
        new(db, new HttpClient(handler), new PassthroughProtector(), new NoOpNotificationService(), NullLogger<InstagramTokenRefreshJob>.Instance);

    [Fact]
    public async Task RunAsync_ChannelWithinRefreshWindow_RefreshesTokenAndExtendsExpiry()
    {
        var db = CreateDb();
        var channel = SeedChannel(db, DateTimeOffset.UtcNow.AddDays(5));
        var handler = new FakeHttpMessageHandler(_ =>
            JsonResponse("""{"access_token":"new-token","token_type":"bearer","expires_in":5184000}"""));

        await MakeJob(db, handler).RunAsync(CancellationToken.None);

        var reloaded = await db.Channels.SingleAsync();
        Assert.Equal(1, handler.CallCount);
        Assert.Contains("new-token", reloaded.CredentialsEncrypted);
        Assert.False(reloaded.RequiresReconnect);
        Assert.True(reloaded.CredentialsExpiresAt > DateTimeOffset.UtcNow.AddDays(50));
    }

    [Fact]
    public async Task RunAsync_ChannelFarFromExpiry_NeverCallsMeta()
    {
        var db = CreateDb();
        SeedChannel(db, DateTimeOffset.UtcNow.AddDays(30));
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("should not be called"));

        await MakeJob(db, handler).RunAsync(CancellationToken.None);

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task RunAsync_RefreshFailsAndTokenAlreadyExpired_SetsRequiresReconnect()
    {
        var db = CreateDb();
        SeedChannel(db, DateTimeOffset.UtcNow.AddDays(-1));
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":{"message":"token invalid"}}"""),
        });

        await MakeJob(db, handler).RunAsync(CancellationToken.None);

        var reloaded = await db.Channels.SingleAsync();
        Assert.True(reloaded.RequiresReconnect);
    }

    [Fact]
    public async Task RunAsync_RefreshFailsButNotYetExpired_DoesNotFlagYet()
    {
        // Still inside the 10-day window but not past expiry — the job retries tomorrow, no need
        // to alarm the operator with RequiresReconnect over one failed attempt.
        var db = CreateDb();
        SeedChannel(db, DateTimeOffset.UtcNow.AddDays(5));
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await MakeJob(db, handler).RunAsync(CancellationToken.None);

        var reloaded = await db.Channels.SingleAsync();
        Assert.False(reloaded.RequiresReconnect);
    }

    [Fact]
    public async Task RunAsync_SuccessfulRefresh_ClearsAPreviouslySetRequiresReconnect()
    {
        var db = CreateDb();
        var channel = SeedChannel(db, DateTimeOffset.UtcNow.AddDays(5));
        channel.RequiresReconnect = true;
        db.SaveChanges();
        var handler = new FakeHttpMessageHandler(_ =>
            JsonResponse("""{"access_token":"new-token","expires_in":5184000}"""));

        await MakeJob(db, handler).RunAsync(CancellationToken.None);

        var reloaded = await db.Channels.SingleAsync();
        Assert.False(reloaded.RequiresReconnect);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
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
}
