using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Features.DataDeletion;

namespace Office.Api.Tests.Features.DataDeletion;

public class DataDeletionJobTests
{
    private readonly AppDbContext _db =
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private readonly string _uploads = Path.Combine(Path.GetTempPath(), "dd-job-" + Guid.NewGuid());

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Office.Api.Tests";
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private DataDeletionJob Job() => new(
        _db,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Uploads:RootPath"] = _uploads }).Build(),
        new FakeWebHostEnvironment(),
        NullLogger<DataDeletionJob>.Instance);

    private Channel AddChannel(string externalId, string? appScopedId, Guid? owner = null)
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(), Type = ChannelType.Instagram, Name = externalId, ExternalId = externalId,
            MetaAppScopedUserId = appScopedId, CustomerId = owner,
        };
        _db.Channels.Add(channel);
        _db.Flows.Add(new Flow
        {
            Id = Guid.NewGuid(), ChannelId = channel.Id, Name = "f", TriggerType = "instagram_dm", TriggerConfigJson = "{}",
        });
        return channel;
    }

    private DataDeletionRequest AddRequest(string metaUserId)
    {
        var request = new DataDeletionRequest
        {
            Id = Guid.NewGuid(), Provider = "instagram", MetaUserId = metaUserId,
            ConfirmationCode = Guid.NewGuid().ToString("N"), SignedRequestHash = Guid.NewGuid().ToString("N"),
        };
        _db.DataDeletionRequests.Add(request);
        _db.SaveChanges();
        return request;
    }

    [Fact]
    public async Task MatchesByAppScopedId_ErasesOnlyThatChannel_AndForgetsTheUserId()
    {
        var target = AddChannel("biz-1", "app-1", owner: Guid.NewGuid());
        var other = AddChannel("biz-2", "app-2");
        var request = AddRequest("app-1");
        var mediaFolder = Path.Combine(_uploads, "whatsapp-media", target.Id.ToString());
        Directory.CreateDirectory(mediaFolder);

        await Job().RunAsync(request.Id, CancellationToken.None);

        Assert.Equal([other.Id], await _db.Channels.Select(c => c.Id).ToListAsync());
        Assert.Single(await _db.Flows.ToListAsync());
        var done = await _db.DataDeletionRequests.SingleAsync();
        Assert.Equal(DataDeletionStatus.Completed, done.Status);
        Assert.Equal(1, done.ChannelsDeleted);
        Assert.Null(done.MetaUserId);
        Assert.False(Directory.Exists(mediaFolder));
    }

    [Fact]
    public async Task MatchesByBusinessIdToo()
    {
        AddChannel("biz-1", appScopedId: null); // connected before app-scoped ids were recorded
        var request = AddRequest("biz-1");

        await Job().RunAsync(request.Id, CancellationToken.None);

        Assert.Empty(await _db.Channels.ToListAsync());
    }

    [Fact]
    public async Task UnknownUser_CompletesWithNothingDeleted()
    {
        AddChannel("biz-1", "app-1");
        var request = AddRequest("someone-else");

        await Job().RunAsync(request.Id, CancellationToken.None);

        Assert.Single(await _db.Channels.ToListAsync());
        Assert.Equal(DataDeletionStatus.Completed, (await _db.DataDeletionRequests.SingleAsync()).Status);
    }

    [Fact]
    public async Task RunningTwice_DoesNothingTheSecondTime()
    {
        AddChannel("biz-1", "app-1");
        var request = AddRequest("app-1");
        await Job().RunAsync(request.Id, CancellationToken.None);

        // The same account is reconnected afterwards; a re-run of the old request must not touch it.
        AddChannel("biz-1", "app-1");
        _db.SaveChanges();
        await Job().RunAsync(request.Id, CancellationToken.None);

        Assert.Single(await _db.Channels.ToListAsync());
    }
}
