using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Office.Api.Channels;
using Office.Api.Channels.Instagram;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Realtime;

namespace Office.Api.Tests.Channels.Instagram;

/// <summary>
/// CheckFollowStatusAsync бо HttpMessageHandler-и сохта. Тамаркуз: кэши интихобии (Following/Unknown кэш
/// мешавад, NotFollowing НЕ — санҷиши зинда 2026-09-15 нишон дод, ки NotFollowing-и кэшшуда
/// корбареро, ки ҳамон лаҳза обуна шудааст, ҷазо медиҳад) ва буҷаи соатӣ.
/// </summary>
public class InstagramProviderFollowCheckTests
{
    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Channel MakeChannel(AppDbContext db)
    {
        var channel = new Channel
        {
            Id = Guid.CreateVersion7(),
            Type = ChannelType.Instagram,
            Name = "Test IG channel",
            ExternalId = "17841400000000000",
            CredentialsEncrypted = """{"instagramAccountId":"17841400000000000","accessToken":"tok"}""",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Channels.Add(channel);
        db.SaveChanges();
        return channel;
    }

    private static InstagramProvider MakeProvider(
        AppDbContext db, FakeHttpMessageHandler handler, IMemoryCache? cache = null, InstagramFollowCheckRateLimiter? rateLimiter = null) =>
        new(
            new HttpClient(handler), new PassthroughProtector(), new ConfigurationBuilder().Build(), db,
            new NoOpNotificationService(), cache ?? new MemoryCache(new MemoryCacheOptions()),
            rateLimiter ?? new InstagramFollowCheckRateLimiter(), NullLogger<InstagramProvider>.Instance);

    private static HttpResponseMessage FollowResponse(bool following) => new(HttpStatusCode.OK)
    {
        Content = new StringContent($$"""{"username":"u","is_user_follow_business":{{(following ? "true" : "false")}}}""", Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task CheckFollowStatusAsync_Following_IsCachedOnSecondCall()
    {
        var db = CreateDb();
        var channel = MakeChannel(db);
        var handler = new FakeHttpMessageHandler(_ => FollowResponse(following: true));
        var provider = MakeProvider(db, handler);

        var first = await provider.CheckFollowStatusAsync(channel, "actor-1", CancellationToken.None);
        var second = await provider.CheckFollowStatusAsync(channel, "actor-1", CancellationToken.None);

        Assert.Equal(FollowCheckResult.Following, first);
        Assert.Equal(FollowCheckResult.Following, second);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task CheckFollowStatusAsync_NotFollowing_IsNeverCached()
    {
        var db = CreateDb();
        var channel = MakeChannel(db);
        var handler = new FakeHttpMessageHandler(_ => FollowResponse(following: false));
        var provider = MakeProvider(db, handler);

        var first = await provider.CheckFollowStatusAsync(channel, "actor-1", CancellationToken.None);
        var second = await provider.CheckFollowStatusAsync(channel, "actor-1", CancellationToken.None);

        Assert.Equal(FollowCheckResult.NotFollowing, first);
        Assert.Equal(FollowCheckResult.NotFollowing, second);
        // Ду дархости воқеӣ — на як, чунки натиҷаи "обуна нест" ҳеҷ гоҳ кэш намешавад.
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task CheckFollowStatusAsync_UnknownFromError_IsCached()
    {
        var db = CreateDb();
        var channel = MakeChannel(db);
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":{"message":"nope","code":100}}""", Encoding.UTF8, "application/json"),
        });
        var provider = MakeProvider(db, handler);

        var first = await provider.CheckFollowStatusAsync(channel, "actor-1", CancellationToken.None);
        var second = await provider.CheckFollowStatusAsync(channel, "actor-1", CancellationToken.None);

        Assert.Equal(FollowCheckResult.Unknown, first);
        Assert.Equal(FollowCheckResult.Unknown, second);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task CheckFollowStatusAsync_HttpThrows_ReturnsUnknownWithoutCrashing()
    {
        var db = CreateDb();
        var channel = MakeChannel(db);
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("network down"));
        var provider = MakeProvider(db, handler);

        var result = await provider.CheckFollowStatusAsync(channel, "actor-1", CancellationToken.None);

        Assert.Equal(FollowCheckResult.Unknown, result);
    }

    [Fact]
    public async Task CheckFollowStatusAsync_DifferentActors_EachGetsOwnCacheEntry()
    {
        var db = CreateDb();
        var channel = MakeChannel(db);
        var handler = new FakeHttpMessageHandler(_ => FollowResponse(following: true));
        var provider = MakeProvider(db, handler);

        await provider.CheckFollowStatusAsync(channel, "actor-1", CancellationToken.None);
        await provider.CheckFollowStatusAsync(channel, "actor-2", CancellationToken.None);

        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task CheckFollowStatusAsync_HourlyBudgetExhausted_ReturnsUnknownWithoutCallingMeta()
    {
        var db = CreateDb();
        var channel = MakeChannel(db);
        var handler = new FakeHttpMessageHandler(_ => FollowResponse(following: true));
        // InstagramProvider's internal budget is a fixed private const (160) — pre-fill the
        // same limiter instance to that level so the provider's own call becomes the 161st.
        var rateLimiter = new InstagramFollowCheckRateLimiter();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 160; i++)
            rateLimiter.TryConsume(limit: 160, now);
        var provider = MakeProvider(db, handler, rateLimiter: rateLimiter);

        var result = await provider.CheckFollowStatusAsync(channel, "actor-1", CancellationToken.None);

        Assert.Equal(FollowCheckResult.Unknown, result);
        Assert.Equal(0, handler.CallCount);
    }

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
