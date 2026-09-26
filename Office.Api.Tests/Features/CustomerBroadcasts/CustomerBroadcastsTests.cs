using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Office.Api.Auth;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Features.CustomerBroadcasts;

namespace Office.Api.Tests.Features.CustomerBroadcasts;

/// <summary>
/// The broadcasts endpoints called as мизоҷ A, through a DbContext with A's real tenant filter.
/// B's broadcasts, channels, flows and contacts — and the company's — must be invisible to every
/// endpoint: not listed, not counted, not usable for a new broadcast, not cancellable or
/// deletable (404, left untouched). The plan is checked only after that (403), and only to create.
/// </summary>
public class CustomerBroadcastsTests
{
    private sealed class FixedTenant(TenantIdentity identity) : ITenantContext
    {
        public TenantIdentity Current => identity;
    }

    private sealed class RecordingJobs : IBackgroundJobClient
    {
        public List<(Guid BroadcastId, IState State)> Created { get; } = [];
        public List<(string JobId, IState State)> Changed { get; } = [];

        public string Create(Job job, IState state)
        {
            Created.Add(((Guid)job.Args[0], state));
            return $"job-{Created.Count}";
        }

        public bool ChangeState(string jobId, IState state, string? expectedState)
        {
            Changed.Add((jobId, state));
            return true;
        }
    }

    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly IConfiguration _configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Subscriptions:Plans:0:Tier"] = "Pro",
            ["Subscriptions:Plans:0:MonthlyPrice"] = "200",
            ["Subscriptions:Plans:0:Limits:ActiveAutomations"] = "10",
        })
        .Build();

    private readonly Guid _customerA = Guid.NewGuid();
    private readonly Guid _customerB = Guid.NewGuid();
    private readonly Guid _channelA = Guid.NewGuid();
    private readonly Guid _channelA2 = Guid.NewGuid(); // A's second Instagram account
    private readonly Guid _channelB = Guid.NewGuid();
    private readonly Guid _companyChannel = Guid.NewGuid();
    private readonly Guid _a1 = Guid.NewGuid(); // "vip", window open
    private readonly Guid _a2 = Guid.NewGuid(); // no tag, window open
    private readonly Guid _a3 = Guid.NewGuid(); // "vip", window closed
    private readonly Guid _b1 = Guid.NewGuid(); // B's: "vip", window open, same Instagram id as a1
    private readonly Guid _c1 = Guid.NewGuid(); // the company's: "vip", window open
    private readonly Guid _flowA = Guid.NewGuid();
    private readonly Guid _flowAOff = Guid.NewGuid();
    private readonly Guid _flowA2 = Guid.NewGuid(); // on A's second account
    private readonly Guid _flowB = Guid.NewGuid();
    private readonly Guid _broadcastA = Guid.NewGuid();
    private readonly Guid _broadcastB = Guid.NewGuid();
    private readonly Guid _broadcastCompany = Guid.NewGuid();

    public CustomerBroadcastsTests()
    {
        using var db = Everything();
        db.Customers.AddRange(
            new Customer { Id = _customerA, Email = "a@example.com", FullName = "A", TrialEndsAt = DateTimeOffset.UtcNow.AddDays(5) },
            new Customer { Id = _customerB, Email = "b@example.com", FullName = "B", TrialEndsAt = DateTimeOffset.UtcNow.AddDays(5) });
        db.Channels.AddRange(
            Channel(_channelA, _customerA), Channel(_channelA2, _customerA), Channel(_channelB, _customerB), Channel(_companyChannel, null));
        db.Conversations.AddRange(
            Contact(_a1, _channelA, "fan-1", "Нилуфар", open: true),
            Contact(_a2, _channelA, "fan-2", "Сино", open: true),
            Contact(_a3, _channelA, "fan-3", "Парвиз", open: false),
            Contact(_b1, _channelB, "fan-1", "Нилуфар Б", open: true),
            Contact(_c1, _companyChannel, "fan-9", "Ширкат", open: true));
        db.ContactTags.AddRange(Tag(_a1, "vip"), Tag(_a3, "vip"), Tag(_b1, "vip"), Tag(_c1, "vip"));
        db.Flows.AddRange(
            Flow(_flowA, _channelA, active: true), Flow(_flowAOff, _channelA, active: false),
            Flow(_flowA2, _channelA2, active: true), Flow(_flowB, _channelB, active: true));

        db.Broadcasts.AddRange(
            Finished(_broadcastA, _channelA, "A-ин"),
            Finished(_broadcastB, _channelB, "B-ин"),
            Finished(_broadcastCompany, _companyChannel, "Ширкат"));
        var longAgo = DateTimeOffset.UtcNow.AddDays(-3);
        db.BroadcastRecipients.AddRange(
            new BroadcastRecipient { BroadcastId = _broadcastA, ContactId = _a1, Status = BroadcastRecipientStatus.Sent, SentAt = longAgo },
            new BroadcastRecipient { BroadcastId = _broadcastA, ContactId = _a2, Status = BroadcastRecipientStatus.Failed, Error = "Паём фиристода нашуд." },
            new BroadcastRecipient { BroadcastId = _broadcastB, ContactId = _b1, Status = BroadcastRecipientStatus.Failed, Error = "B-ro" },
            new BroadcastRecipient { BroadcastId = _broadcastCompany, ContactId = _c1, Status = BroadcastRecipientStatus.Sent, SentAt = longAgo });
        db.SaveChanges();
    }

    private static Channel Channel(Guid id, Guid? owner) =>
        new() { Id = id, Type = ChannelType.Instagram, Name = id.ToString()[..6], ExternalId = id.ToString(), CustomerId = owner, IsActive = true };

    private static Conversation Contact(Guid id, Guid channelId, string externalId, string name, bool open) => new()
    {
        Id = id, ChannelId = channelId, ExternalId = externalId, ContactName = name, ContactUsername = externalId,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        WindowExpiresAt = DateTimeOffset.UtcNow.AddHours(open ? 3 : -3),
    };

    private static ContactTag Tag(Guid contactId, string tag) => new() { ContactId = contactId, Tag = tag, CreatedAt = DateTimeOffset.UtcNow };

    private static Flow Flow(Guid id, Guid channelId, bool active) => new()
    {
        Id = id, ChannelId = channelId, Name = $"flow-{id.ToString()[..4]}", IsActive = active, TriggerType = "instagram_dm",
        TriggerConfigJson = "{}", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static Broadcast Finished(Guid id, Guid channelId, string name) => new()
    {
        Id = id, ChannelId = channelId, Name = name, TagsJson = """["vip"]""", Text = "Салом", Status = BroadcastStatus.Finished,
        ScheduledAt = DateTimeOffset.UtcNow.AddDays(-3), StartedAt = DateTimeOffset.UtcNow.AddDays(-3),
        FinishedAt = DateTimeOffset.UtcNow.AddDays(-3), CreatedAt = DateTimeOffset.UtcNow.AddDays(-3), AudienceCount = 3,
    };

    private AppDbContext Open(TenantIdentity? tenant) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_databaseName).Options,
        tenant is null ? null : new FixedTenant(tenant.Value));

    private AppDbContext AsA() => Open(new TenantIdentity(TenantScope.Customer, _customerA));
    private AppDbContext Everything() => Open(tenant: null);

    private ClaimsPrincipal PrincipalA => new(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, _customerA.ToString())], "test"));

    private static int? Status(IResult result) => (result as IStatusCodeHttpResult)?.StatusCode;

    private void Change(Action<AppDbContext> change)
    {
        using var db = Everything();
        change(db);
        db.SaveChanges();
    }

    private Guid AddBroadcast(Guid channelId, BroadcastStatus status, string? jobId = null)
    {
        var id = Guid.NewGuid();
        Change(db => db.Broadcasts.Add(new Broadcast
        {
            Id = id, ChannelId = channelId, Name = "n", Text = "t", Status = status, JobId = jobId,
            ScheduledAt = DateTimeOffset.UtcNow.AddHours(1), CreatedAt = DateTimeOffset.UtcNow,
        }));
        return id;
    }

    private static CreateBroadcastRequest Message(Guid channelId, DateTimeOffset? at = null, string[]? tags = null) =>
        new(channelId, "Аксияи тирамоҳ", tags ?? ["vip"], "Салом, {{firstName}}!", null, null, null, null, null, at);

    private async Task<(IResult Result, RecordingJobs Jobs)> Create(CreateBroadcastRequest request)
    {
        var jobs = new RecordingJobs();
        await using var db = AsA();
        var result = await CustomerBroadcastsEndpoints.CreateAsync(request, PrincipalA, db, _configuration, jobs, CancellationToken.None);
        return (result, jobs);
    }

    private int BroadcastCount()
    {
        using var db = Everything();
        return db.Broadcasts.Count();
    }

    // ── Seeing ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_ShowsOnlyTheCallersBroadcasts_WithTheirCounts()
    {
        await using var db = AsA();
        var result = await CustomerBroadcastsEndpoints.ListAsync(null, db, CancellationToken.None);

        var item = Assert.Single(Assert.IsType<Ok<List<BroadcastListItem>>>(result).Value!);
        Assert.Equal(_broadcastA, item.Id);
        Assert.Equal((2, 1, 1, 1), (item.Recipients, item.Sent, item.Failed, item.NotReachable)); // audience 3, 2 planned
        Assert.Equal("message", item.Kind);
        Assert.Equal("Finished", item.Status);
    }

    [Fact]
    public async Task List_ByAnotherMizojsOrTheCompanysChannel_IsEmpty()
    {
        await using var db = AsA();
        foreach (var channelId in new[] { _channelB, _companyChannel })
        {
            var result = await CustomerBroadcastsEndpoints.ListAsync(channelId, db, CancellationToken.None);
            Assert.Empty(Assert.IsType<Ok<List<BroadcastListItem>>>(result).Value!);
        }
    }

    [Fact]
    public async Task Card_ShowsItsFailuresByName()
    {
        await using var db = AsA();
        var result = await CustomerBroadcastsEndpoints.GetAsync(_broadcastA, db, CancellationToken.None);

        var detail = Assert.IsType<Ok<BroadcastDetail>>(result).Value!;
        var failure = Assert.Single(detail.Failures);
        Assert.Equal((_a2, "Сино", "Паём фиристода нашуд."), (failure.ContactId, failure.Name, failure.Error));
        Assert.Equal(["vip"], detail.Tags);
        Assert.Equal("Салом", detail.Text);
    }

    [Fact]
    public async Task Card_OfAnotherMizojsOrTheCompanysBroadcast_IsNotFound()
    {
        await using var db = AsA();
        Assert.Equal(404, Status(await CustomerBroadcastsEndpoints.GetAsync(_broadcastB, db, CancellationToken.None)));
        Assert.Equal(404, Status(await CustomerBroadcastsEndpoints.GetAsync(_broadcastCompany, db, CancellationToken.None)));
    }

    [Fact]
    public async Task Audience_CountsOnlyTheCallersOwnContacts()
    {
        await using var db = AsA();
        var vip = await CustomerBroadcastsEndpoints.AudienceAsync(_channelA, ["vip"], db, CancellationToken.None);
        var everyone = await CustomerBroadcastsEndpoints.AudienceAsync(_channelA, null, db, CancellationToken.None);

        Assert.Equal(new BroadcastAudience(2, 1), Assert.IsType<Ok<BroadcastAudience>>(vip).Value); // a1, a3 — B's and the company's "vip" are not A's
        Assert.Equal(new BroadcastAudience(3, 2), Assert.IsType<Ok<BroadcastAudience>>(everyone).Value);
    }

    [Fact]
    public async Task Audience_OfAnotherMizojsOrTheCompanysChannel_IsNotFound()
    {
        await using var db = AsA();
        Assert.Equal(404, Status(await CustomerBroadcastsEndpoints.AudienceAsync(_channelB, ["vip"], db, CancellationToken.None)));
        Assert.Equal(404, Status(await CustomerBroadcastsEndpoints.AudienceAsync(_companyChannel, null, db, CancellationToken.None)));
    }

    // ── Creating ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_OnAnotherMizojsOrTheCompanysChannel_IsNotFound_AndNothingIsCreated()
    {
        var before = BroadcastCount();

        var (toB, jobsB) = await Create(Message(_channelB));
        var (toCompany, jobsCompany) = await Create(Message(_companyChannel));

        Assert.Equal(404, Status(toB));
        Assert.Equal(404, Status(toCompany));
        Assert.Equal(before, BroadcastCount());
        Assert.Empty(jobsB.Created);
        Assert.Empty(jobsCompany.Created);
    }

    [Fact]
    public async Task Create_WithAnotherMizojsFlow_AnotherAccountsFlow_OrAnOffOne_IsRefused()
    {
        var before = BroadcastCount();
        var withFlow = (Guid flowId) => new CreateBroadcastRequest(_channelA, "F", null, null, null, null, null, null, flowId, null);

        Assert.Equal(400, Status((await Create(withFlow(_flowB))).Result));
        Assert.Equal(400, Status((await Create(withFlow(_flowA2))).Result)); // A's own, but of the other account
        Assert.Equal(400, Status((await Create(withFlow(_flowAOff))).Result));
        Assert.Equal(before, BroadcastCount());
    }

    [Fact]
    public async Task Create_WithoutAPlan_IsForbidden_ButAnotherMizojsChannelIsStillNotFound()
    {
        Change(db => db.Customers.Single(c => c.Id == _customerA).TrialEndsAt = DateTimeOffset.UtcNow.AddDays(-1));
        var before = BroadcastCount();

        Assert.Equal(403, Status((await Create(Message(_channelA))).Result));
        Assert.Equal(404, Status((await Create(Message(_channelB))).Result)); // who owns what first, then the plan
        Assert.Equal(before, BroadcastCount());
    }

    [Fact]
    public async Task Create_OnADisconnectedAccount_IsRefused()
    {
        Change(db => db.Channels.Single(c => c.Id == _channelA).RequiresReconnect = true);

        Assert.Equal(409, Status((await Create(Message(_channelA))).Result));
    }

    [Fact]
    public async Task Create_Now_QueuesTheSendingAtOnce()
    {
        var (result, jobs) = await Create(Message(_channelA, tags: [" vip ", "vip", ""]));

        var created = Assert.IsType<Created<BroadcastListItem>>(result).Value!;
        Assert.Equal(("Scheduled", "message"), (created.Status, created.Kind));
        var (broadcastId, state) = Assert.Single(jobs.Created);
        Assert.Equal(created.Id, broadcastId);
        Assert.IsType<EnqueuedState>(state);

        await using var db = Everything();
        var saved = await db.Broadcasts.SingleAsync(b => b.Id == created.Id);
        Assert.Equal(_channelA, saved.ChannelId);
        Assert.Equal("job-1", saved.JobId);
        Assert.Equal("""["vip"]""", saved.TagsJson); // trimmed, once, no blanks
    }

    [Fact]
    public async Task Create_Later_SchedulesItForThatTime()
    {
        var at = DateTimeOffset.UtcNow.AddHours(2);

        var (result, jobs) = await Create(Message(_channelA, at));

        Assert.IsType<Created<BroadcastListItem>>(result);
        var state = Assert.IsType<ScheduledState>(Assert.Single(jobs.Created).State);
        Assert.InRange(state.EnqueueAt, at.UtcDateTime.AddSeconds(-1), at.UtcDateTime.AddSeconds(1));
    }

    [Fact]
    public async Task Create_AFlowBroadcast_KeepsNoMessage()
    {
        var request = new CreateBroadcastRequest(
            _channelA, "Flow", null, null, null, "data:image/png;base64,AAAA", null, null, _flowA, null);

        var (result, _) = await Create(request);

        var created = Assert.IsType<Created<BroadcastListItem>>(result).Value!;
        Assert.Equal("flow", created.Kind);
        await using var db = Everything();
        var saved = await db.Broadcasts.SingleAsync(b => b.Id == created.Id);
        Assert.Equal(_flowA, saved.FlowId);
        Assert.Null(saved.MediaPreviewDataUri);
        Assert.Null(saved.Text);
    }

    [Fact]
    public async Task Create_TheSixthWaitingOnOneAccount_IsRefused_OtherAccountsDoNotCount()
    {
        for (var i = 0; i < 5; i++)
        {
            AddBroadcast(_channelA2, BroadcastStatus.Scheduled); // A's other account has its own five
            AddBroadcast(_channelB, BroadcastStatus.Scheduled); // and B's never count for A
        }
        for (var i = 0; i < 4; i++)
            AddBroadcast(_channelA, i % 2 == 0 ? BroadcastStatus.Scheduled : BroadcastStatus.Sending);

        Assert.Equal(201, Status((await Create(Message(_channelA))).Result));
        Assert.Equal(409, Status((await Create(Message(_channelA))).Result));
    }

    // ── Cancelling and deleting ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancel_AnotherMizojsBroadcast_IsNotFound_AndUntouched()
    {
        var theirs = AddBroadcast(_channelB, BroadcastStatus.Scheduled, jobId: "their-job");
        var jobs = new RecordingJobs();

        await using (var db = AsA())
            Assert.Equal(404, Status(await CustomerBroadcastsEndpoints.CancelAsync(theirs, db, jobs, CancellationToken.None)));

        Assert.Empty(jobs.Changed);
        await using var all = Everything();
        var broadcast = await all.Broadcasts.SingleAsync(b => b.Id == theirs);
        Assert.Equal(BroadcastStatus.Scheduled, broadcast.Status);
        Assert.False(broadcast.StopRequested);
    }

    [Fact]
    public async Task Cancel_AWaitingOne_RemovesItsJob()
    {
        var id = AddBroadcast(_channelA, BroadcastStatus.Scheduled, jobId: "job-42");
        var jobs = new RecordingJobs();

        await using (var db = AsA())
            Assert.Equal(204, Status(await CustomerBroadcastsEndpoints.CancelAsync(id, db, jobs, CancellationToken.None)));

        var (jobId, state) = Assert.Single(jobs.Changed);
        Assert.Equal("job-42", jobId);
        Assert.IsType<DeletedState>(state);
        await using var all = Everything();
        var broadcast = await all.Broadcasts.SingleAsync(b => b.Id == id);
        Assert.Equal(BroadcastStatus.Cancelled, broadcast.Status);
        Assert.True(broadcast.StopRequested);
    }

    [Fact]
    public async Task Cancel_ASendingOne_AsksItToStop()
    {
        var id = AddBroadcast(_channelA, BroadcastStatus.Sending);
        var jobs = new RecordingJobs();

        await using (var db = AsA())
            Assert.Equal(204, Status(await CustomerBroadcastsEndpoints.CancelAsync(id, db, jobs, CancellationToken.None)));

        await using var all = Everything();
        Assert.True((await all.Broadcasts.SingleAsync(b => b.Id == id)).StopRequested);
        var (broadcastId, state) = Assert.Single(jobs.Created); // a run that only closes it
        Assert.Equal(id, broadcastId);
        Assert.IsType<EnqueuedState>(state);
    }

    [Fact]
    public async Task Stop_PressedTwice_QueuesOneClosingRun()
    {
        var id = AddBroadcast(_channelA, BroadcastStatus.Sending);
        var jobs = new RecordingJobs();

        await using (var db = AsA())
        {
            Assert.Equal(204, Status(await CustomerBroadcastsEndpoints.CancelAsync(id, db, jobs, CancellationToken.None)));
            Assert.Equal(204, Status(await CustomerBroadcastsEndpoints.CancelAsync(id, db, jobs, CancellationToken.None)));
        }

        Assert.Single(jobs.Created);
    }

    [Fact]
    public async Task Cancel_AFinishedOne_IsConflict()
    {
        await using var db = AsA();
        Assert.Equal(409, Status(await CustomerBroadcastsEndpoints.CancelAsync(_broadcastA, db, new RecordingJobs(), CancellationToken.None)));
    }

    [Fact]
    public async Task Delete_AnotherMizojsBroadcast_IsNotFound_AndUntouched()
    {
        await using (var db = AsA())
        {
            Assert.Equal(404, Status(await CustomerBroadcastsEndpoints.DeleteAsync(_broadcastB, db, CancellationToken.None)));
            Assert.Equal(404, Status(await CustomerBroadcastsEndpoints.DeleteAsync(_broadcastCompany, db, CancellationToken.None)));
        }

        await using var all = Everything();
        Assert.True(await all.Broadcasts.AnyAsync(b => b.Id == _broadcastB));
        Assert.True(await all.BroadcastRecipients.AnyAsync(r => r.BroadcastId == _broadcastB));
        Assert.True(await all.Broadcasts.AnyAsync(b => b.Id == _broadcastCompany));
    }

    [Fact]
    public async Task Delete_AFinishedOne_RemovesItWithItsRecipients()
    {
        await using (var db = AsA())
            Assert.Equal(204, Status(await CustomerBroadcastsEndpoints.DeleteAsync(_broadcastA, db, CancellationToken.None)));

        await using var all = Everything();
        Assert.False(await all.Broadcasts.AnyAsync(b => b.Id == _broadcastA));
        Assert.False(await all.BroadcastRecipients.AnyAsync(r => r.BroadcastId == _broadcastA));
        Assert.True(await all.Conversations.AnyAsync(c => c.Id == _a1)); // the people stay
    }

    [Fact]
    public async Task Delete_AWaitingOrSendingOne_IsConflict()
    {
        var waiting = AddBroadcast(_channelA, BroadcastStatus.Scheduled);
        var sending = AddBroadcast(_channelA, BroadcastStatus.Sending);

        await using var db = AsA();
        Assert.Equal(409, Status(await CustomerBroadcastsEndpoints.DeleteAsync(waiting, db, CancellationToken.None)));
        Assert.Equal(409, Status(await CustomerBroadcastsEndpoints.DeleteAsync(sending, db, CancellationToken.None)));
    }

    // ── Validation ──────────────────────────────────────────────────────────────────────────

    private static readonly CreateBroadcastRequestValidator Validator = new();

    private static CreateBroadcastRequest Valid() =>
        new(Guid.NewGuid(), "Аксия", ["vip"], "Салом", null, null, null, null, null, null);

    [Fact]
    public void Validator_AcceptsAMessage_AnImageWithText_AndAFlow()
    {
        Assert.True(Validator.Validate(Valid()).IsValid);
        Assert.True(Validator.Validate(Valid() with
        {
            MediaId = "123456", MediaPreviewDataUri = "data:image/jpeg;base64,AAAA", ButtonTitle = "Дидан", ButtonUrl = "https://shop.example",
        }).IsValid);
        Assert.True(Validator.Validate(Valid() with { MediaId = "123456", Text = null }).IsValid);
        Assert.True(Validator.Validate(Valid() with { Text = null, FlowId = Guid.NewGuid(), ScheduledAt = DateTimeOffset.UtcNow.AddDays(3) }).IsValid);
    }

    public static TheoryData<string, CreateBroadcastRequest> Invalid => new()
    {
        { "no name", Valid() with { Name = "  " } },
        { "no content", Valid() with { Text = " " } },
        { "a flow and a message", Valid() with { FlowId = Guid.NewGuid() } },
        { "a flow and an image", Valid() with { Text = null, MediaId = "1", FlowId = Guid.NewGuid() } },
        { "text too long", Valid() with { Text = new string('x', 1001) } },
        { "media id not Meta's", Valid() with { MediaId = "../x" } },
        { "preview not an image", Valid() with { MediaId = "1", MediaPreviewDataUri = "data:image/svg+xml;base64,PHN2Zz4=" } },
        { "preview a link", Valid() with { MediaId = "1", MediaPreviewDataUri = "https://evil.example/x.png" } },
        { "button without a link", Valid() with { ButtonTitle = "Дидан" } },
        { "link without a title", Valid() with { ButtonUrl = "https://shop.example" } },
        { "button title too long", Valid() with { ButtonTitle = new string('x', 21), ButtonUrl = "https://shop.example" } },
        { "script link", Valid() with { ButtonTitle = "x", ButtonUrl = "javascript:alert(1)" } },
        { "button with an image only", Valid() with { Text = null, MediaId = "1", ButtonTitle = "x", ButtonUrl = "https://shop.example" } },
        { "too many tags", Valid() with { Tags = Enumerable.Range(0, 21).Select(i => $"t{i}").ToArray() } },
        { "a blank tag", Valid() with { Tags = ["vip", " "] } },
        { "in the past", Valid() with { ScheduledAt = DateTimeOffset.UtcNow.AddMinutes(-10) } },
        { "too far ahead", Valid() with { ScheduledAt = DateTimeOffset.UtcNow.AddDays(31) } },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Validator_Refuses(string why, CreateBroadcastRequest request) =>
        Assert.False(Validator.Validate(request).IsValid, why);

    [Fact]
    public void TagsJson_RoundTrips()
    {
        var broadcast = new Broadcast { Name = "x", TagsJson = JsonSerializer.Serialize(new[] { "vip", "lead" }) };
        Assert.Equal(["vip", "lead"], Office.Api.Channels.Broadcasts.BroadcastSendJob.ReadTags(broadcast));
    }
}
