using System.Net;
using System.Text;
using System.Text.Json;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Office.Api.Channels;
using Office.Api.Channels.Broadcasts;
using Office.Api.Channels.Flows;
using Office.Api.Channels.Instagram;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Realtime;

namespace Office.Api.Tests.Channels.Broadcasts;

/// <summary>
/// BroadcastSendJob as Hangfire runs it: no tenant filter at all (System), so everything that
/// keeps мизоҷон apart is the job's own. мизоҷ B has a contact with the SAME Instagram id and the
/// SAME tag as A's, with an open window — A's broadcast must never reach them. Instagram is a
/// fake HTTP handler that records every request (account, token, body).
/// </summary>
public class BroadcastSendJobTests
{
    private const string AccountA = "17841400000000001";
    private const string AccountB = "17841400000000002";

    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly Guid _customerA = Guid.NewGuid();
    private readonly Guid _customerB = Guid.NewGuid();
    private readonly Guid _channelA = Guid.NewGuid();
    private readonly Guid _channelB = Guid.NewGuid();
    private readonly Guid _a1 = Guid.NewGuid(); // "vip", window open, Instagram id fan-1
    private readonly Guid _a2 = Guid.NewGuid(); // no tag, window open
    private readonly Guid _a3 = Guid.NewGuid(); // "vip", window closed an hour ago
    private readonly Guid _a4 = Guid.NewGuid(); // "vip", never wrote (no window)
    private readonly Guid _b1 = Guid.NewGuid(); // B's: "vip", window open, the same Instagram id fan-1

    public BroadcastSendJobTests()
    {
        using var db = Open();
        db.Customers.AddRange(
            new Customer { Id = _customerA, Email = "a@example.com", FullName = "A", TrialEndsAt = DateTimeOffset.UtcNow.AddDays(5) },
            new Customer { Id = _customerB, Email = "b@example.com", FullName = "B", TrialEndsAt = DateTimeOffset.UtcNow.AddDays(5) });
        db.Channels.AddRange(Channel(_channelA, _customerA, AccountA, "token-a"), Channel(_channelB, _customerB, AccountB, "token-b"));
        db.Conversations.AddRange(
            Contact(_a1, _channelA, "fan-1", "Нилуфар Ахмедова", window: TimeSpan.FromHours(3)),
            Contact(_a2, _channelA, "fan-2", "Сино", window: TimeSpan.FromHours(3)),
            Contact(_a3, _channelA, "fan-3", "Парвиз", window: TimeSpan.FromHours(-1)),
            Contact(_a4, _channelA, "fan-4", "Мадина", window: null),
            Contact(_b1, _channelB, "fan-1", "Нилуфар Каримова", window: TimeSpan.FromHours(3)));
        db.ContactTags.AddRange(Tag(_a1, "vip"), Tag(_a3, "vip"), Tag(_a4, "vip"), Tag(_b1, "vip"));
        db.ContactVariables.AddRange(
            new ContactVariable { ContactId = _a1, Key = "promo", Value = "X1" },
            new ContactVariable { ContactId = _b1, Key = "promo", Value = "B-SECRET" });
        db.SaveChanges();
    }

    // ── Fakes ───────────────────────────────────────────────────────────────────────────────

    private sealed record GraphRequest(string Url, string? Authorization, string Body)
    {
        public JsonElement Json => JsonDocument.Parse(Body).RootElement;
        public string Recipient => Json.GetProperty("recipient").GetProperty("id").GetString()!;
    }

    private sealed class FakeGraph(Func<GraphRequest, HttpResponseMessage?>? respond) : HttpMessageHandler
    {
        public List<GraphRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var recorded = new GraphRequest(request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), body);
            Requests.Add(recorded);
            return respond?.Invoke(recorded) ?? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"recipient_id":"x","message_id":"mid.1"}""", Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class PassthroughProtector : IChannelCredentialsProtector
    {
        public string Protect(string plainText) => plainText;
        public string Unprotect(string protectedText) => protectedText;
    }

    private sealed class NoNotifications : INotificationService
    {
        public Task PushAsync(Guid userId, string type, object payload, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class RecordingJobs : IBackgroundJobClient
    {
        public List<(Guid BroadcastId, IState State)> Created { get; } = [];

        public string Create(Job job, IState state)
        {
            Created.Add(((Guid)job.Args[0], state));
            return $"job-{Created.Count}";
        }

        public bool ChangeState(string jobId, IState state, string? expectedState) => true;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────

    private AppDbContext Open() => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_databaseName).Options);

    private static Channel Channel(Guid id, Guid owner, string account, string token) => new()
    {
        Id = id, Type = ChannelType.Instagram, Name = account, ExternalId = account, CustomerId = owner, IsActive = true,
        CredentialsEncrypted = JsonSerializer.Serialize(new { instagramAccountId = account, accessToken = token }),
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static Conversation Contact(Guid id, Guid channelId, string externalId, string name, TimeSpan? window) => new()
    {
        Id = id, ChannelId = channelId, ExternalId = externalId, ContactName = name, ContactUsername = externalId,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        WindowExpiresAt = window is null ? null : DateTimeOffset.UtcNow + window.Value,
    };

    private static ContactTag Tag(Guid contactId, string tag) => new() { ContactId = contactId, Tag = tag, CreatedAt = DateTimeOffset.UtcNow };

    private Guid AddBroadcast(Guid? channelId = null, string[]? tags = null, Action<Broadcast>? configure = null)
    {
        using var db = Open();
        var broadcast = new Broadcast
        {
            Id = Guid.CreateVersion7(),
            ChannelId = channelId ?? _channelA,
            Name = "Аксия",
            TagsJson = JsonSerializer.Serialize(tags ?? ["vip"]),
            Text = "Салом, {{firstName}}! Код: {{promo}}",
            ScheduledAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        configure?.Invoke(broadcast);
        db.Broadcasts.Add(broadcast);
        db.SaveChanges();
        return broadcast.Id;
    }

    /// <summary>A broadcast already sending, with these people still waiting.</summary>
    private Guid AddSendingBroadcast(params Guid[] pending)
    {
        var id = AddBroadcast(configure: b => { b.Status = BroadcastStatus.Sending; b.StartedAt = DateTimeOffset.UtcNow; });
        using var db = Open();
        db.BroadcastRecipients.AddRange(pending.Select(c => new BroadcastRecipient { BroadcastId = id, ContactId = c }));
        db.SaveChanges();
        return id;
    }

    private void Change(Action<AppDbContext> change)
    {
        using var db = Open();
        change(db);
        db.SaveChanges();
    }

    private async Task<(FakeGraph Graph, RecordingJobs Jobs)> Run(
        Guid broadcastId, Func<GraphRequest, HttpResponseMessage?>? respond = null, FakeGraph? graph = null, RecordingJobs? jobs = null)
    {
        graph ??= new FakeGraph(respond);
        jobs ??= new RecordingJobs();
        await using var db = Open();
        var provider = new InstagramProvider(
            new HttpClient(graph), new PassthroughProtector(), new ConfigurationBuilder().Build(), db,
            new NoNotifications(), new MemoryCache(new MemoryCacheOptions()),
            new InstagramFollowCheckRateLimiter(), NullLogger<InstagramProvider>.Instance);
        var engine = new FlowEngine(db, provider, jobs, new HttpClient(graph), NullLogger<FlowEngine>.Instance);
        var job = new BroadcastSendJob(db, provider, engine, jobs, NullLogger<BroadcastSendJob>.Instance);
        await job.RunAsync(broadcastId, CancellationToken.None);
        return (graph, jobs);
    }

    private Broadcast Load(Guid id)
    {
        using var db = Open();
        return db.Broadcasts.AsNoTracking().Single(b => b.Id == id);
    }

    private Dictionary<Guid, BroadcastRecipientStatus> Recipients(Guid id)
    {
        using var db = Open();
        return db.BroadcastRecipients.Where(r => r.BroadcastId == id).ToDictionary(r => r.ContactId, r => r.Status);
    }

    private static HttpResponseMessage GraphError(int code) => new(HttpStatusCode.BadRequest)
    {
        Content = new StringContent($$$"""{"error":{"message":"refused","type":"OAuthException","code":{{{code}}}}}""", Encoding.UTF8, "application/json"),
    };

    // ── Isolation ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReachesOnlyTheBroadcastsOwnAccount_NeverAnotherMizojsContactWithTheSameInstagramIdAndTag()
    {
        var idA = AddBroadcast();
        var (graphA, _) = await Run(idA);

        var sent = Assert.Single(graphA.Requests);
        Assert.Contains($"/{AccountA}/messages", sent.Url);
        Assert.Equal("Bearer token-a", sent.Authorization);
        Assert.Equal("fan-1", sent.Recipient);
        Assert.Equal([_a1], Recipients(idA).Keys);
        Assert.DoesNotContain("B-SECRET", sent.Body); // B's details never fill A's message

        // And the other way round: B's broadcast, B's account and token, B's person only.
        var idB = AddBroadcast(channelId: _channelB);
        var (graphB, _) = await Run(idB);

        var sentB = Assert.Single(graphB.Requests);
        Assert.Contains($"/{AccountB}/messages", sentB.Url);
        Assert.Equal("Bearer token-b", sentB.Authorization);
        Assert.Equal([_b1], Recipients(idB).Keys);
    }

    [Fact]
    public async Task ARecipientRowOfAnotherAccount_IsNeverSentTo()
    {
        // Defense in depth: even a row pointing at B's contact (it can only be planted) goes nowhere.
        var id = AddSendingBroadcast(_b1);

        var (graph, _) = await Run(id);

        Assert.Empty(graph.Requests);
        Assert.Equal(BroadcastRecipientStatus.Failed, Recipients(id)[_b1]);
    }

    [Fact]
    public async Task AFlowOfAnotherAccount_FailsTheBroadcast_AndStartsNothing()
    {
        var flowB = new Flow
        {
            Id = Guid.NewGuid(), ChannelId = _channelB, Name = "B", IsActive = true, TriggerType = "instagram_dm",
            TriggerConfigJson = "{}", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        Change(db => db.Flows.Add(flowB));
        var id = AddBroadcast(configure: b => { b.Text = null; b.FlowId = flowB.Id; });

        var (graph, _) = await Run(id);

        Assert.Empty(graph.Requests);
        Assert.Equal(BroadcastStatus.Failed, Load(id).Status);
        await using var db = Open();
        Assert.False(await db.FlowSessions.AnyAsync());
    }

    // ── Who is reached ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OnlyOpenWindows_TheClosedAndTheNeverOpenedAreCountedButNotSent()
    {
        var id = AddBroadcast();

        await Run(id);

        var broadcast = Load(id);
        Assert.Equal(3, broadcast.AudienceCount); // a1, a3, a4 — B's "vip" is not A's audience
        Assert.Equal([_a1], Recipients(id).Keys);
        Assert.Equal(BroadcastStatus.Finished, broadcast.Status);
        Assert.NotNull(broadcast.StartedAt);
        Assert.NotNull(broadcast.FinishedAt);
    }

    [Fact]
    public async Task NoTags_MeansEveryoneOfTheAccount()
    {
        var id = AddBroadcast(tags: []);

        var (graph, _) = await Run(id);

        Assert.Equal(4, Load(id).AudienceCount);
        Assert.Equal(["fan-1", "fan-2"], graph.Requests.Select(r => r.Recipient).Order());
    }

    [Fact]
    public async Task TheWindowIsCheckedAgainRightBeforeEachMessage()
    {
        // Planned while open; by the time their turn came the window had closed (a3) or never opened (a4).
        var id = AddSendingBroadcast(_a1, _a3, _a4);

        var (graph, _) = await Run(id);

        Assert.Equal(["fan-1"], graph.Requests.Select(r => r.Recipient));
        var recipients = Recipients(id);
        Assert.Equal(BroadcastRecipientStatus.Sent, recipients[_a1]);
        Assert.Equal(BroadcastRecipientStatus.SkippedWindowClosed, recipients[_a3]);
        Assert.Equal(BroadcastRecipientStatus.SkippedWindowClosed, recipients[_a4]);
    }

    [Fact]
    public async Task OneBroadcastPerPersonPer24Hours()
    {
        var earlier = AddBroadcast(configure: b => b.Status = BroadcastStatus.Finished);
        Change(db => db.BroadcastRecipients.Add(new BroadcastRecipient
        {
            BroadcastId = earlier, ContactId = _a1, Status = BroadcastRecipientStatus.Sent, SentAt = DateTimeOffset.UtcNow.AddHours(-2),
        }));

        // Not planned at all…
        var id = AddBroadcast();
        var (graph, _) = await Run(id);
        Assert.Empty(graph.Requests);
        Assert.Empty(Recipients(id));

        // …and if already planned, skipped right before sending.
        var planned = AddSendingBroadcast(_a1);
        var (graph2, _) = await Run(planned);
        Assert.Empty(graph2.Requests);
        Assert.Equal(BroadcastRecipientStatus.SkippedRecentlyMessaged, Recipients(planned)[_a1]);
    }

    [Fact]
    public async Task After24Hours_ThePersonCanBeReachedAgain()
    {
        var earlier = AddBroadcast(configure: b => b.Status = BroadcastStatus.Finished);
        Change(db => db.BroadcastRecipients.Add(new BroadcastRecipient
        {
            BroadcastId = earlier, ContactId = _a1, Status = BroadcastRecipientStatus.Sent, SentAt = DateTimeOffset.UtcNow.AddHours(-25),
        }));
        var id = AddBroadcast();

        var (graph, _) = await Run(id);

        Assert.Equal(["fan-1"], graph.Requests.Select(r => r.Recipient));
    }

    // ── What is sent ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheTextIsPersonal_AndNeverCarriesAMessageTag()
    {
        var id = AddBroadcast();

        var (graph, _) = await Run(id);

        var sent = Assert.Single(graph.Requests).Json;
        Assert.Equal("Салом, Нилуфар! Код: X1", sent.GetProperty("message").GetProperty("text").GetString());
        Assert.Equal("RESPONSE", sent.GetProperty("messaging_type").GetString());
        Assert.False(sent.TryGetProperty("tag", out _));
    }

    [Fact]
    public async Task ImageThenTextWithALinkButton_NoMessageTagOnAnyOfThem()
    {
        var id = AddBroadcast(configure: b =>
        {
            b.MediaId = "555";
            b.Text = "Нав омад, {{firstName}}";
            b.ButtonTitle = "Дидан";
            b.ButtonUrl = "https://shop.example/new";
        });

        var (graph, _) = await Run(id);

        Assert.Equal(2, graph.Requests.Count);
        var image = graph.Requests[0].Json.GetProperty("message").GetProperty("attachment");
        Assert.Equal("image", image.GetProperty("type").GetString());
        Assert.Equal("555", image.GetProperty("payload").GetProperty("attachment_id").GetString());
        var template = graph.Requests[1].Json.GetProperty("message").GetProperty("attachment").GetProperty("payload");
        Assert.Equal("Нав омад, Нилуфар", template.GetProperty("text").GetString());
        var button = template.GetProperty("buttons")[0];
        Assert.Equal("web_url", button.GetProperty("type").GetString());
        Assert.Equal("https://shop.example/new", button.GetProperty("url").GetString());
        Assert.All(graph.Requests, r =>
        {
            Assert.Equal("RESPONSE", r.Json.GetProperty("messaging_type").GetString());
            Assert.False(r.Json.TryGetProperty("tag", out _));
        });
    }

    [Fact]
    public async Task AFlowBroadcast_StartsTheFlowForEachPerson()
    {
        var flow = new Flow
        {
            Id = Guid.NewGuid(), ChannelId = _channelA, Name = "Нарх", IsActive = true, TriggerType = "instagram_dm",
            TriggerConfigJson = "{}", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        Change(db =>
        {
            db.Flows.Add(flow);
            db.FlowNodes.Add(new FlowNode
            {
                Id = Guid.NewGuid(), FlowId = flow.Id, Type = FlowNodeType.Message,
                ConfigJson = JsonSerializer.Serialize(new MessageNodeConfig([new MessageBlock(MessageBlock.TypeText, "Аз flow, {{firstName}}", null)], [])),
            });
        });
        var id = AddBroadcast(configure: b => { b.Text = null; b.FlowId = flow.Id; });

        var (graph, _) = await Run(id);

        var sent = Assert.Single(graph.Requests);
        Assert.Equal("Аз flow, Нилуфар", sent.Json.GetProperty("message").GetProperty("text").GetString());
        Assert.False(sent.Json.TryGetProperty("tag", out _));
        Assert.Equal(BroadcastRecipientStatus.Sent, Recipients(id)[_a1]);
        await using var db = Open();
        Assert.Equal(_a1, (await db.FlowSessions.SingleAsync()).ContactId);
    }

    [Fact]
    public async Task AnEmptyFlow_IsAFailureForThePerson_NotASend()
    {
        var flow = new Flow
        {
            Id = Guid.NewGuid(), ChannelId = _channelA, Name = "Холӣ", IsActive = true, TriggerType = "instagram_dm",
            TriggerConfigJson = "{}", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        Change(db => db.Flows.Add(flow));
        var id = AddBroadcast(configure: b => { b.Text = null; b.FlowId = flow.Id; });

        await Run(id);

        Assert.Equal(BroadcastRecipientStatus.Failed, Recipients(id)[_a1]);
    }

    // ── Pace, stop, failures ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendsTenAtATime_ThenTheNextBatch15SecondsLater()
    {
        Change(db =>
        {
            for (var i = 0; i < 24; i++)
            {
                var contactId = Guid.NewGuid();
                db.Conversations.Add(Contact(contactId, _channelA, $"many-{i}", $"Одам {i}", window: TimeSpan.FromHours(3)));
                db.ContactTags.Add(Tag(contactId, "vip"));
            }
        });
        var id = AddBroadcast(); // 25 reachable: a1 + 24

        var graph = new FakeGraph(null);
        var (_, jobs1) = await Run(id, graph: graph);
        Assert.Equal(10, graph.Requests.Count);
        Assert.Equal(BroadcastStatus.Sending, Load(id).Status);
        var next = Assert.IsType<ScheduledState>(Assert.Single(jobs1.Created).State);
        Assert.InRange(next.EnqueueAt - DateTime.UtcNow, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(16));

        await Run(id, graph: graph);
        Assert.Equal(20, graph.Requests.Count);

        var (_, jobs3) = await Run(id, graph: graph);
        Assert.Equal(25, graph.Requests.Count);
        Assert.Empty(jobs3.Created);
        Assert.Equal(BroadcastStatus.Finished, Load(id).Status);
        Assert.Equal(25, graph.Requests.Select(r => r.Recipient).Distinct().Count()); // nobody twice
    }

    [Fact]
    public async Task ARerun_NeverSendsToAnyoneTwice()
    {
        var id = AddSendingBroadcast(_a1, _a2);
        Change(db => db.BroadcastRecipients.Single(r => r.BroadcastId == id && r.ContactId == _a1).Status = BroadcastRecipientStatus.Sent);

        var (graph, _) = await Run(id);
        Assert.Equal(["fan-2"], graph.Requests.Select(r => r.Recipient));

        var (again, _) = await Run(id); // finished — a second run does nothing
        Assert.Empty(again.Requests);
    }

    [Fact]
    public async Task Stop_CancelsEveryoneStillWaiting_AndSendsNothing()
    {
        var id = AddSendingBroadcast(_a1, _a2);
        Change(db => db.Broadcasts.Single(b => b.Id == id).StopRequested = true);

        var (graph, _) = await Run(id);

        Assert.Empty(graph.Requests);
        Assert.Equal(BroadcastStatus.Cancelled, Load(id).Status);
        Assert.All(Recipients(id).Values, s => Assert.Equal(BroadcastRecipientStatus.Cancelled, s));
    }

    [Fact]
    public async Task CancelledBeforeItStarted_PlansNoOne()
    {
        var id = AddBroadcast(configure: b => b.StopRequested = true); // cancelled just as its job was starting

        var (graph, _) = await Run(id);

        Assert.Empty(graph.Requests);
        Assert.Equal(BroadcastStatus.Cancelled, Load(id).Status);
        Assert.Empty(Recipients(id)); // nobody listed as "cancelled" — it never began
    }

    [Fact]
    public async Task AStopPressedMidBatch_TakesEffectBeforeTheNextMessage()
    {
        var id = AddSendingBroadcast(_a1, _a2);

        var (graph, _) = await Run(id, respond: _ =>
        {
            Change(db => db.Broadcasts.Single(b => b.Id == id).StopRequested = true); // pressed while the first was going out
            return null;
        });

        Assert.Single(graph.Requests);
        Assert.Equal(BroadcastStatus.Cancelled, Load(id).Status);
        // Whoever went first got it; the other one is cancelled (the order is by id).
        Assert.Equal([BroadcastRecipientStatus.Sent, BroadcastRecipientStatus.Cancelled], Recipients(id).Values.Order());
    }

    [Fact]
    public async Task APersonDeletedWhileTheirMessageWasGoing_DoesNotFailTheBroadcast()
    {
        var id = AddSendingBroadcast(_a1, _a2);
        var deleted = false;

        var (graph, _) = await Run(id, respond: _ =>
        {
            if (deleted)
                return null;
            deleted = true; // the мизоҷ deleted this person while their message was being sent
            Change(db =>
            {
                var first = db.BroadcastRecipients.Where(r => r.BroadcastId == id).OrderBy(r => r.ContactId).First();
                db.BroadcastRecipients.Remove(first);
            });
            return null;
        });

        Assert.Equal(2, graph.Requests.Count);
        Assert.Equal(BroadcastStatus.Finished, Load(id).Status);
        Assert.Equal([BroadcastRecipientStatus.Sent], Recipients(id).Values);
    }

    [Fact]
    public async Task NoPlan_FailsTheBroadcastWithoutSending()
    {
        Change(db => db.Customers.Single(c => c.Id == _customerA).TrialEndsAt = DateTimeOffset.UtcNow.AddDays(-1));
        var id = AddBroadcast();

        var (graph, _) = await Run(id);

        Assert.Empty(graph.Requests);
        var broadcast = Load(id);
        Assert.Equal(BroadcastStatus.Failed, broadcast.Status);
        Assert.Contains("Тариф", broadcast.Error);
    }

    [Fact]
    public async Task ADisconnectedAccount_FailsTheBroadcastWithoutSending()
    {
        Change(db => db.Channels.Single(c => c.Id == _channelA).RequiresReconnect = true);
        var id = AddSendingBroadcast(_a1);

        var (graph, _) = await Run(id);

        Assert.Empty(graph.Requests);
        Assert.Equal(BroadcastStatus.Failed, Load(id).Status);
        Assert.Equal(BroadcastRecipientStatus.Cancelled, Recipients(id)[_a1]);
    }

    [Fact]
    public async Task ARefusedToken_StopsTheBroadcastAtOnce()
    {
        var id = AddSendingBroadcast(_a1, _a2);

        var (graph, _) = await Run(id, respond: _ => GraphError(190));

        Assert.Single(graph.Requests); // not tried again for the next person
        Assert.Equal(BroadcastStatus.Failed, Load(id).Status);
        Assert.Equal([BroadcastRecipientStatus.Failed, BroadcastRecipientStatus.Cancelled], Recipients(id).Values.Order());
    }

    [Fact]
    public async Task OnePersonRefused_IsRecorded_AndTheOthersStillGetIt()
    {
        var id = AddSendingBroadcast(_a1, _a2);

        await Run(id, respond: r => r.Recipient == "fan-1" ? GraphError(10) : null);

        await using var db = Open();
        var failed = await db.BroadcastRecipients.SingleAsync(r => r.BroadcastId == id && r.ContactId == _a1);
        Assert.Equal(BroadcastRecipientStatus.Failed, failed.Status);
        Assert.False(string.IsNullOrEmpty(failed.Error));
        Assert.Equal(BroadcastRecipientStatus.Sent, Recipients(id)[_a2]);
        Assert.Equal(BroadcastStatus.Finished, Load(id).Status);
    }

    [Fact]
    public async Task WaitsWhileAnotherBroadcastOfTheSameAccountIsSending()
    {
        AddSendingBroadcast(_a2);
        var id = AddBroadcast();

        var (graph, jobs) = await Run(id);

        Assert.Empty(graph.Requests);
        Assert.Equal(BroadcastStatus.Scheduled, Load(id).Status);
        Assert.Empty(Recipients(id));
        Assert.IsType<ScheduledState>(Assert.Single(jobs.Created).State);
    }

    [Fact]
    public async Task AnotherMizojsSendingBroadcast_DoesNotHoldThisOneBack()
    {
        var other = AddBroadcast(channelId: _channelB, configure: b => b.Status = BroadcastStatus.Sending);
        Change(db => db.BroadcastRecipients.Add(new BroadcastRecipient { BroadcastId = other, ContactId = _b1 }));
        var id = AddBroadcast();

        var (graph, _) = await Run(id);

        Assert.Equal(["fan-1"], graph.Requests.Select(r => r.Recipient));
        Assert.Contains($"/{AccountA}/", graph.Requests[0].Url);
    }

    [Fact]
    public async Task AnUnexpectedError_ClosesTheBroadcastAsFailed_NotSendingForever()
    {
        var id = AddBroadcast(configure: b => b.TagsJson = "not json");

        var (graph, _) = await Run(id);

        Assert.Empty(graph.Requests);
        Assert.Equal(BroadcastStatus.Failed, Load(id).Status);
    }

    [Fact]
    public async Task AClosedBroadcast_IsNeverRunAgain()
    {
        var id = AddBroadcast(configure: b => b.Status = BroadcastStatus.Cancelled);

        var (graph, jobs) = await Run(id);

        Assert.Empty(graph.Requests);
        Assert.Empty(jobs.Created);
        Assert.Empty(Recipients(id));
    }
}
