using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Office.Api.Channels;
using Office.Api.Channels.Instagram;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Realtime;

namespace Office.Api.Tests.Integration;

/// <summary>
/// Old Instagram conversations created before ContactUsername existed only show a raw IGSID —
/// this job backfills them. These tests exercise the actual RunAsync against an EF InMemory db
/// and a fake HttpMessageHandler, same spirit as InstagramTokenRefreshJobTests.
/// </summary>
public class InstagramContactProfileBackfillJobTests
{
    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static (Channel Channel, Conversation Conversation) SeedConversation(
        AppDbContext db, string externalId, string? contactUsername = null, DateTimeOffset? fetchedAt = null)
    {
        var channel = db.Channels.FirstOrDefault(c => c.Type == ChannelType.Instagram)
            ?? new Channel
            {
                Id = Guid.CreateVersion7(),
                Type = ChannelType.Instagram,
                Name = "Test IG channel",
                ExternalId = "17841400000000000",
                CredentialsEncrypted = """{"instagramAccountId":"17841400000000000","accessToken":"tok"}""",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            };
        if (db.Entry(channel).State == EntityState.Detached)
            db.Channels.Add(channel);

        var conversation = new Conversation
        {
            Id = Guid.CreateVersion7(),
            ChannelId = channel.Id,
            Channel = channel,
            ExternalId = externalId,
            Status = ConversationStatus.New,
            ContactUsername = contactUsername,
            ContactProfileFetchedAt = fetchedAt,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Conversations.Add(conversation);
        db.SaveChanges();
        return (channel, conversation);
    }

    private static InstagramContactProfileBackfillJob MakeJob(AppDbContext db, FakeHttpMessageHandler handler)
    {
        var provider = new InstagramProvider(
            new HttpClient(handler), new PassthroughProtector(), new ConfigurationBuilder().Build(),
            db, new NoOpNotificationService(), NullLogger<InstagramProvider>.Instance);
        return new InstagramContactProfileBackfillJob(db, provider, NullLogger<InstagramContactProfileBackfillJob>.Instance);
    }

    [Fact]
    public async Task RunAsync_ConversationWithoutUsername_FillsNameUsernameAndAvatar()
    {
        var db = CreateDb();
        var (_, conversation) = SeedConversation(db, "1111");
        var handler = new FakeHttpMessageHandler(_ =>
            JsonResponse("""{"name":"Daler","username":"the_cinecut","profile_pic":"https://cdn.example/pic.jpg"}"""));

        await MakeJob(db, handler).RunAsync(CancellationToken.None);

        var reloaded = await db.Conversations.SingleAsync(c => c.Id == conversation.Id);
        Assert.Equal("Daler", reloaded.ContactName);
        Assert.Equal("the_cinecut", reloaded.ContactUsername);
        Assert.Equal("https://cdn.example/pic.jpg", reloaded.ContactAvatarUrl);
        Assert.NotNull(reloaded.ContactProfileFetchedAt);
    }

    [Fact]
    public async Task RunAsync_AlreadyHasUsername_IsNeverQueried()
    {
        var db = CreateDb();
        SeedConversation(db, "2222", contactUsername: "already_set");
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("should not be called"));

        await MakeJob(db, handler).RunAsync(CancellationToken.None);

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task RunAsync_SecondRun_IsIdempotent_NoRefetch()
    {
        var db = CreateDb();
        SeedConversation(db, "3333");
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("""{"name":"Daler","username":"daler_ig"}"""));

        await MakeJob(db, handler).RunAsync(CancellationToken.None);
        Assert.Equal(1, handler.CallCount);

        await MakeJob(db, handler).RunAsync(CancellationToken.None);

        Assert.Equal(1, handler.CallCount); // second run found nothing left to do
    }

    [Fact]
    public async Task RunAsync_MetaGivesNoProfile_MarksFetchedButLeavesFieldsNull_AndDoesNotRetry()
    {
        // e.g. the customer deleted their account or left — Meta responds but with nothing useful.
        var db = CreateDb();
        var (_, conversation) = SeedConversation(db, "4444");
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("{}"));

        await MakeJob(db, handler).RunAsync(CancellationToken.None);

        var reloaded = await db.Conversations.SingleAsync(c => c.Id == conversation.Id);
        Assert.Null(reloaded.ContactUsername);
        Assert.NotNull(reloaded.ContactProfileFetchedAt); // marked so it's never retried

        await MakeJob(db, handler).RunAsync(CancellationToken.None);
        Assert.Equal(1, handler.CallCount); // confirmed not retried
    }

    [Fact]
    public async Task RunAsync_OneConversationThrows_OthersStillProcessed_AndTheFailedOneStaysRetryable()
    {
        var db = CreateDb();
        var (_, failing) = SeedConversation(db, "5555");
        var (_, healthy) = SeedConversation(db, "6666");
        var callCount = 0;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            callCount++;
            if (callCount == 1)
                throw new HttpRequestException("network blip");
            return JsonResponse("""{"username":"healthy_one"}""");
        });

        await MakeJob(db, handler).RunAsync(CancellationToken.None);

        var reloadedFailing = await db.Conversations.SingleAsync(c => c.Id == failing.Id);
        var reloadedHealthy = await db.Conversations.SingleAsync(c => c.Id == healthy.Id);
        Assert.Null(reloadedFailing.ContactProfileFetchedAt); // eligible for retry next run
        Assert.Equal("healthy_one", reloadedHealthy.ContactUsername);
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
