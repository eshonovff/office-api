using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Office.Api.Auth;
using Office.Api.Channels;
using Office.Api.Channels.Instagram;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Features.CommentAutomation;
using Office.Api.Realtime;

namespace Office.Api.Tests.Features.Flows;

/// <summary>
/// Phase 20: the active-stories list behind the "reply to a story" picker. One handler for staff
/// and мизоҷ — the channel is looked up through the tenant filter, so a мизоҷ gets only their own
/// channels' stories (another's and the company's are "not found"), staff never a мизоҷ's.
/// </summary>
public sealed class InstagramStoriesEndpointTests
{
    private sealed class FixedTenant(TenantIdentity identity) : ITenantContext
    {
        public TenantIdentity Current => identity;
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

    private sealed class Meta(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private const string TwoStories = """
        {"data":[
          {"id":"17900000000000001","media_type":"IMAGE","media_url":"https://scontent.cdninstagram.com/s1.jpg","permalink":"https://www.instagram.com/stories/a.shop/1/","timestamp":"2026-09-27T08:00:00+0000"},
          {"id":"17900000000000002","media_type":"VIDEO","media_url":"https://scontent.cdninstagram.com/s2.mp4","thumbnail_url":"https://scontent.cdninstagram.com/s2.jpg","timestamp":"2026-09-27T09:00:00+0000"}
        ]}
        """;

    private readonly string _name = Guid.NewGuid().ToString();
    private readonly Guid _customerA = Guid.NewGuid();
    private readonly Guid _customerB = Guid.NewGuid();
    private readonly Guid _channelA = Guid.NewGuid();
    private readonly Guid _channelB = Guid.NewGuid();
    private readonly Guid _company = Guid.NewGuid();
    private readonly Guid _facebookA = Guid.NewGuid();

    public InstagramStoriesEndpointTests()
    {
        using var db = Open(null);
        db.Channels.AddRange(
            Channel(_channelA, _customerA, ChannelType.Instagram, "17841400000000001"),
            Channel(_channelB, _customerB, ChannelType.Instagram, "17841400000000002"),
            Channel(_company, null, ChannelType.Instagram, "17841400000000003"),
            Channel(_facebookA, _customerA, ChannelType.Facebook, "10000000000000004"));
        db.SaveChanges();
    }

    private static Channel Channel(Guid id, Guid? owner, ChannelType type, string accountId) => new()
    {
        Id = id, Type = type, Name = "ig", ExternalId = accountId, CustomerId = owner, IsActive = true,
        CredentialsEncrypted = $$"""{"instagramAccountId":"{{accountId}}","accessToken":"tok-{{accountId}}"}""",
    };

    private AppDbContext Open(TenantIdentity? tenant) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_name).Options,
        tenant is null ? null : new FixedTenant(tenant.Value));

    private AppDbContext AsA() => Open(new TenantIdentity(TenantScope.Customer, _customerA));
    private AppDbContext AsStaff() => Open(new TenantIdentity(TenantScope.Staff, null));

    private static async Task<IResult> ListAsync(AppDbContext db, Guid channelId, Meta meta, IMemoryCache? cache = null)
    {
        cache ??= new MemoryCache(new MemoryCacheOptions());
        var provider = new InstagramProvider(
            new HttpClient(meta), new PassthroughProtector(), new ConfigurationBuilder().Build(), db,
            new NoOpNotificationService(), cache, new InstagramFollowCheckRateLimiter(), NullLogger<InstagramProvider>.Instance);
        return await CommentAutomationEndpoints.ListInstagramStoriesAsync(
            channelId, db, provider, cache, NullLogger<Program>.Instance, CancellationToken.None);
    }

    private static int? Status(IResult result) => (result as IStatusCodeHttpResult)?.StatusCode;

    [Fact]
    public async Task AMizoj_GetsTheirOwnChannelsStories()
    {
        await using var db = AsA();
        var meta = new Meta(HttpStatusCode.OK, TwoStories);

        var result = Assert.IsType<Ok<InstagramMediaListResult>>(await ListAsync(db, _channelA, meta));

        Assert.Equal(["17900000000000001", "17900000000000002"], result.Value!.Items.Select(i => i.Id));
        Assert.Equal("https://scontent.cdninstagram.com/s2.jpg", result.Value.Items[1].ImageUrl); // a video shows its picture
        Assert.Null(result.Value.NextCursor);
        var url = Assert.Single(meta.Urls);
        Assert.Contains("/17841400000000001/stories?", url); // this channel's account, the stories edge
    }

    [Fact]
    public async Task AMizoj_GetsNothingOfAnothersOrTheCompanys_AndMetaIsNeverAsked()
    {
        await using var db = AsA();
        var meta = new Meta(HttpStatusCode.OK, TwoStories);

        Assert.Equal(404, Status(await ListAsync(db, _channelB, meta)));
        Assert.Equal(404, Status(await ListAsync(db, _company, meta)));
        Assert.Equal(404, Status(await ListAsync(db, _facebookA, meta))); // not Instagram
        Assert.Equal(404, Status(await ListAsync(db, Guid.NewGuid(), meta)));
        Assert.Empty(meta.Urls);
    }

    [Fact]
    public async Task Staff_GetTheCompanysStories_NeverAMizojs()
    {
        await using var db = AsStaff();
        var meta = new Meta(HttpStatusCode.OK, TwoStories);

        Assert.IsType<Ok<InstagramMediaListResult>>(await ListAsync(db, _company, meta));
        Assert.Equal(404, Status(await ListAsync(db, _channelA, meta)));
        Assert.Single(meta.Urls);
    }

    [Fact]
    public async Task ADisconnectedChannel_Is409Reconnect_AndMetaIsNeverAsked()
    {
        await using (var setup = Open(null))
        {
            var channel = await setup.Channels.SingleAsync(c => c.Id == _channelA);
            channel.CredentialsEncrypted = null; // what disconnecting leaves
            await setup.SaveChangesAsync();
        }

        await using var db = AsA();
        var meta = new Meta(HttpStatusCode.OK, TwoStories);

        Assert.Equal(409, Status(await ListAsync(db, _channelA, meta)));
        Assert.Empty(meta.Urls);
    }

    [Fact]
    public async Task AChannelToReconnect_Is409_AndMetaIsNeverAsked()
    {
        await using (var setup = Open(null))
        {
            (await setup.Channels.SingleAsync(c => c.Id == _channelA)).RequiresReconnect = true;
            await setup.SaveChangesAsync();
        }

        await using var db = AsA();
        var meta = new Meta(HttpStatusCode.OK, TwoStories);

        Assert.Equal(409, Status(await ListAsync(db, _channelA, meta)));
        Assert.Empty(meta.Urls);
    }

    [Fact]
    public async Task TheListIsKeptAMinute_SoThePickerDoesNotHammerMeta()
    {
        await using var db = AsA();
        var meta = new Meta(HttpStatusCode.OK, TwoStories);
        var cache = new MemoryCache(new MemoryCacheOptions());

        await ListAsync(db, _channelA, meta, cache);
        await ListAsync(db, _channelA, meta, cache);

        Assert.Single(meta.Urls);
    }

    [Fact]
    public async Task WhenMetaRefuses_ItIs502_AndNothingIsKept()
    {
        await using var db = AsA();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var refusing = new Meta(HttpStatusCode.BadRequest, """{"error":{"message":"Unsupported get request","code":100}}""");

        Assert.Equal(502, Status(await ListAsync(db, _channelA, refusing, cache)));

        var meta = new Meta(HttpStatusCode.OK, TwoStories);
        Assert.IsType<Ok<InstagramMediaListResult>>(await ListAsync(db, _channelA, meta, cache));
    }
}
