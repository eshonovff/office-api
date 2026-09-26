using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Office.Api.Channels;
using Office.Api.Channels.ContactProfiles;
using Office.Api.Data;
using Office.Api.Data.Entities;

namespace Office.Api.Tests.Channels.ContactProfiles;

/// <summary>
/// The daily job for chats already in the database: which it asks about (Instagram and Facebook,
/// active in 90 days, no name or no picture of ours, not asked in a day, on a working account),
/// newest first; a picture is queued only after its link is saved; one failure stops nothing.
/// </summary>
public class ContactProfileBackfillJobTests
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly FakeProfileProvider _instagram = new(id => id == "throws" ? throw new HttpRequestException("down")
        : new ContactProfile("Name " + id, "https://scontent.cdninstagram.com/" + id + ".jpg", "user_" + id));
    private readonly FakeProfileProvider _facebook = new(id => new ContactProfile("FB " + id, null));
    private readonly Jobs _jobs;

    public ContactProfileBackfillJobTests() => _jobs = new Jobs(_db);

    private sealed class Factory(IChannelProvider instagram, IChannelProvider facebook) : IChannelProviderFactory
    {
        public List<ChannelType> Requested { get; } = [];

        public IChannelProvider GetProvider(ChannelType type)
        {
            Requested.Add(type);
            return type switch
            {
                ChannelType.Instagram => instagram,
                ChannelType.Facebook => facebook,
                _ => new FakeProfileProvider(), // WhatsApp has no profile API — must never be asked
            };
        }
    }

    private sealed class Jobs(AppDbContext db) : IBackgroundJobClient
    {
        public List<(Guid Id, bool LinkSaved)> Pictures { get; } = [];

        public string Create(Job job, IState state)
        {
            var id = (Guid)job.Args[0]!;
            Pictures.Add((id, db.Conversations.AsNoTracking().Any(c => c.Id == id && c.ContactAvatarUrl != null)));
            return "job";
        }

        public bool ChangeState(string jobId, IState state, string? expectedState) => true;
    }

    private Channel Account(ChannelType type, bool reconnect = false, bool active = true, string? credentials = "token")
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(), Type = type, Name = type.ToString(), ExternalId = Guid.NewGuid().ToString(), IsActive = active,
            RequiresReconnect = reconnect, CredentialsEncrypted = credentials,
        };
        _db.Channels.Add(channel);
        return channel;
    }

    private Conversation Chat(Channel channel, string externalId, int daysSinceLastMessage = 1, string? username = null, string? name = null,
        string? picture = null, TimeSpan? askedAgo = null)
    {
        var chat = new Conversation
        {
            Id = Guid.NewGuid(), ChannelId = channel.Id, ExternalId = externalId, ContactUsername = username, ContactName = name,
            ContactAvatarPath = picture, CreatedAt = DateTimeOffset.UtcNow.AddDays(-200),
            LastMessageAt = DateTimeOffset.UtcNow.AddDays(-daysSinceLastMessage),
            ContactProfileFetchedAt = askedAgo is null ? null : DateTimeOffset.UtcNow - askedAgo,
        };
        _db.Conversations.Add(chat);
        return chat;
    }

    private readonly List<ChannelType> _requested = [];

    private Task Run()
    {
        var factory = new Factory(_instagram, _facebook);
        return new ContactProfileBackfillJob(_db, factory, _jobs, NullLogger<ContactProfileBackfillJob>.Instance) { Pause = TimeSpan.Zero }
            .RunAsync(CancellationToken.None)
            .ContinueWith(t => { _requested.AddRange(factory.Requested); t.GetAwaiter().GetResult(); });
    }

    [Fact]
    public async Task AsksOnlyAboutChatsThatNeedIt_NewestFirst()
    {
        var ig = Account(ChannelType.Instagram);
        var fb = Account(ChannelType.Facebook);
        Chat(ig, "nameless", daysSinceLastMessage: 3);
        Chat(ig, "no-picture", daysSinceLastMessage: 1, username: "x", name: "X", askedAgo: TimeSpan.FromDays(2));
        Chat(ig, "complete", username: "x", name: "X", picture: "p.jpg");
        Chat(ig, "asked-an-hour-ago", askedAgo: TimeSpan.FromHours(1));
        Chat(ig, "quiet-for-100-days", daysSinceLastMessage: 100);
        Chat(Account(ChannelType.WhatsApp), "whatsapp");
        Chat(Account(ChannelType.Instagram, reconnect: true), "must-reconnect");
        Chat(Account(ChannelType.Instagram, active: false), "disconnected");
        Chat(Account(ChannelType.Instagram, credentials: null), "no-token");
        Chat(fb, "facebook", daysSinceLastMessage: 2);
        await _db.SaveChangesAsync();

        await Run();

        Assert.Equal(["no-picture", "nameless"], _instagram.Asked);
        Assert.Equal(["facebook"], _facebook.Asked);
        Assert.DoesNotContain(ChannelType.WhatsApp, _requested);
        var nameless = await _db.Conversations.AsNoTracking().SingleAsync(c => c.ExternalId == "nameless");
        Assert.Equal(("Name nameless", "user_nameless"), (nameless.ContactName, nameless.ContactUsername));
    }

    [Fact]
    public async Task APictureIsQueuedOnlyAfterItsLinkIsSaved_AndOneFailureStopsNothing()
    {
        var ig = Account(ChannelType.Instagram);
        Chat(ig, "throws", daysSinceLastMessage: 1);
        var ok = Chat(ig, "ok", daysSinceLastMessage: 2);
        await _db.SaveChangesAsync();

        await Run();

        Assert.Equal([(ok.Id, true)], _jobs.Pictures);
        Assert.Equal(["throws", "ok"], _instagram.Asked);

        // The failed one waits a day — it never blocks the queue in front of the others.
        await Run();
        Assert.Equal(["throws", "ok"], _instagram.Asked);
    }

    [Fact]
    public async Task ASecondRunTheSameDay_AsksNobodyAgain()
    {
        var ig = Account(ChannelType.Instagram);
        _instagram.Answer = _ => ContactProfile.Empty; // Meta still says nothing
        Chat(ig, "nameless");
        await _db.SaveChangesAsync();

        await Run();
        await Run();

        Assert.Single(_instagram.Asked);
    }
}
