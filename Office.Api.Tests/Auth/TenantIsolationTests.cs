using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Data;
using Office.Api.Data.Entities;

namespace Office.Api.Tests.Auth;

/// <summary>
/// AppDbContext's tenant query filters, table by table: staff see only company data, a мизоҷ
/// only their own, an unrecognised caller nothing. One full "channel tree" is seeded per owner
/// (company, мизоҷ A, мизоҷ B).
/// </summary>
public class TenantIsolationTests
{
    private static readonly Guid CustomerA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CustomerB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private sealed class FixedTenant(TenantIdentity identity) : ITenantContext
    {
        public TenantIdentity Current => identity;
    }

    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly Dictionary<string, Guid> _channelIds = [];

    // Every seeded row id (and every id a row points to) → owner tag, so query results made
    // of ids can be mapped back to whose rows they are.
    private readonly Dictionary<Guid, string> _ownerOf = [];

    public TenantIsolationTests()
    {
        using var db = Open(tenant: null); // System — seeding sees and writes everything.
        var staffUser = new User { Id = Guid.NewGuid(), FullName = "Staff", Username = "staff", PasswordHash = "x" };
        db.Users.Add(staffUser);
        db.Customers.AddRange(
            new Customer { Id = CustomerA, Email = "a@example.com", FullName = "A" },
            new Customer { Id = CustomerB, Email = "b@example.com", FullName = "B" });

        _channelIds["company"] = SeedChannelTree(db, "company", ownerId: null, staffUser.Id);
        _channelIds["a"] = SeedChannelTree(db, "a", CustomerA, staffUser.Id);
        _channelIds["b"] = SeedChannelTree(db, "b", CustomerB, staffUser.Id);
        db.SaveChanges();
    }

    private AppDbContext Open(TenantIdentity? tenant) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_databaseName).Options,
        tenant is null ? null : new FixedTenant(tenant.Value));

    private Guid Own(Guid id, string tag)
    {
        _ownerOf[id] = tag;
        return id;
    }

    private Guid SeedChannelTree(AppDbContext db, string tag, Guid? ownerId, Guid staffUserId)
    {
        var now = DateTimeOffset.UtcNow;
        var channelId = Own(Guid.NewGuid(), tag);
        var conversationId = Own(Guid.NewGuid(), tag);
        var ruleId = Own(Guid.NewGuid(), tag);
        var flowId = Own(Guid.NewGuid(), tag);
        var nodeId = Own(Guid.NewGuid(), tag);
        var sessionId = Own(Guid.NewGuid(), tag);

        db.Channels.Add(new Channel
        {
            Id = channelId, Type = ChannelType.Instagram, Name = tag, ExternalId = $"ig-{tag}",
            CustomerId = ownerId, CreatedAt = now,
        });
        db.ChannelMembers.Add(new ChannelMember { ChannelId = channelId, UserId = staffUserId });
        db.Conversations.Add(new Conversation { Id = conversationId, ChannelId = channelId, ExternalId = $"contact-{tag}", CreatedAt = now });
        db.Messages.Add(new Message { Id = Own(Guid.NewGuid(), tag), ConversationId = conversationId, CreatedAt = now });
        db.ConversationAssignmentEvents.Add(new ConversationAssignmentEvent { Id = Own(Guid.NewGuid(), tag), ConversationId = conversationId, CreatedAt = now });
        db.ContactTags.Add(new ContactTag { ContactId = conversationId, Tag = tag, CreatedAt = now });
        db.ContactVariables.Add(new ContactVariable { ContactId = conversationId, Key = "k", Value = tag });
        db.AutomationRules.Add(new AutomationRule
        {
            Id = ruleId, ChannelId = channelId, Name = tag, TriggerType = "instagram_comment",
            TriggerConfigJson = "{}", ActionConfigJson = "{}", CreatedAt = now,
        });
        db.AutomationRuns.Add(new AutomationRun
        {
            Id = Own(Guid.NewGuid(), tag), RuleId = ruleId, TriggerExternalId = $"t-{tag}", ActorExternalId = $"a-{tag}", CreatedAt = now,
        });
        db.Flows.Add(new Flow
        {
            Id = flowId, ChannelId = channelId, Name = tag, TriggerType = "instagram_dm",
            TriggerConfigJson = "{}", CreatedAt = now, UpdatedAt = now,
        });
        db.FlowNodes.Add(new FlowNode { Id = nodeId, FlowId = flowId, Type = FlowNodeType.Message, ConfigJson = "{}" });
        db.FlowEdges.Add(new FlowEdge { Id = Own(Guid.NewGuid(), tag), FlowId = flowId, FromNodeId = nodeId, FromPort = "default", ToNodeId = nodeId });
        db.FlowSessions.Add(new FlowSession { Id = sessionId, FlowId = flowId, ContactId = conversationId, CreatedAt = now });
        db.FlowSessionSteps.Add(new FlowSessionStep { Id = Own(Guid.NewGuid(), tag), SessionId = sessionId, NodeId = nodeId, CreatedAt = now });
        db.InstagramComments.Add(new InstagramComment
        {
            Id = Own(Guid.NewGuid(), tag), ChannelId = channelId, ExternalId = $"comment-{tag}", MediaExternalId = $"media-{tag}",
            AuthorExternalId = $"fan-{tag}", Text = tag, CommentedAt = now, ReceivedAt = now,
        });
        return channelId;
    }

    /// <summary>
    /// What each of the 15 channel-owned tables shows this caller. Only the table's OWN columns
    /// are read — never a navigation: a navigation join applies the parent's filter as well and
    /// would hide a missing filter on the table itself (while a plain Where on ConversationId
    /// in real code would leak). Verified: removing any one table's filter fails these tests.
    /// </summary>
    private Dictionary<string, List<string>> Visible(AppDbContext db)
    {
        List<string> Owners(IQueryable<Guid> ids) => ids.ToList().Select(id => _ownerOf[id]).ToList();

        return new()
        {
            ["channels"] = Owners(db.Channels.Select(c => c.Id)),
            ["channel_members"] = Owners(db.ChannelMembers.Select(m => m.ChannelId)),
            ["conversations"] = Owners(db.Conversations.Select(c => c.Id)),
            ["messages"] = Owners(db.Messages.Select(m => m.Id)),
            ["assignment_events"] = Owners(db.ConversationAssignmentEvents.Select(e => e.Id)),
            ["contact_tags"] = db.ContactTags.Select(t => t.Tag).ToList(),
            ["contact_variables"] = db.ContactVariables.Select(v => v.Value).ToList(),
            ["automation_rules"] = Owners(db.AutomationRules.Select(r => r.Id)),
            ["automation_runs"] = Owners(db.AutomationRuns.Select(r => r.Id)),
            ["flows"] = Owners(db.Flows.Select(f => f.Id)),
            ["flow_nodes"] = Owners(db.FlowNodes.Select(n => n.Id)),
            ["flow_edges"] = Owners(db.FlowEdges.Select(e => e.Id)),
            ["flow_sessions"] = Owners(db.FlowSessions.Select(s => s.Id)),
            ["flow_session_steps"] = Owners(db.FlowSessionSteps.Select(s => s.Id)),
            ["instagram_comments"] = Owners(db.InstagramComments.Select(c => c.Id)),
        };
    }

    private void AssertEveryTableShowsOnly(AppDbContext db, params string[] expectedOwners)
    {
        foreach (var (table, owners) in Visible(db))
            Assert.True(owners.Order().SequenceEqual(expectedOwners.Order()), $"{table}: saw [{string.Join(", ", owners)}]");
    }

    [Fact]
    public void Staff_SeeOnlyCompanyData()
    {
        using var db = Open(new TenantIdentity(TenantScope.Staff, null));
        AssertEveryTableShowsOnly(db, "company");
    }

    [Fact]
    public void Customer_SeesOnlyTheirOwnData()
    {
        using var dbA = Open(new TenantIdentity(TenantScope.Customer, CustomerA));
        AssertEveryTableShowsOnly(dbA, "a");

        using var dbB = Open(new TenantIdentity(TenantScope.Customer, CustomerB));
        AssertEveryTableShowsOnly(dbB, "b");
    }

    [Fact]
    public void UnrecognisedCaller_SeesNothing()
    {
        using var db = Open(new TenantIdentity(TenantScope.None, null));
        AssertEveryTableShowsOnly(db);
    }

    [Fact]
    public void CustomerScopeWithoutId_SeesNothing_NotCompanyData()
    {
        // The Customer branch of the rule requires a known id — otherwise "owner == null"
        // would match every company channel.
        using var db = Open(new TenantIdentity(TenantScope.Customer, null));
        AssertEveryTableShowsOnly(db);
    }

    [Fact]
    public void System_SeesEveryOwner()
    {
        using var db = Open(new TenantIdentity(TenantScope.System, null));
        AssertEveryTableShowsOnly(db, "a", "b", "company");
    }

    [Fact]
    public void LookingUpAnotherOwnersRowsById_FindsNothing()
    {
        // The "guess an id" attack: a known id must not open another owner's data.
        using var staff = Open(new TenantIdentity(TenantScope.Staff, null));
        Assert.Null(staff.Channels.FirstOrDefault(c => c.Id == _channelIds["a"]));
        Assert.Empty(staff.Conversations.Where(c => c.ChannelId == _channelIds["a"]));

        using var customerA = Open(new TenantIdentity(TenantScope.Customer, CustomerA));
        Assert.Null(customerA.Channels.FirstOrDefault(c => c.Id == _channelIds["b"]));
        Assert.Null(customerA.Channels.FirstOrDefault(c => c.Id == _channelIds["company"]));
        Assert.Empty(customerA.Flows.Where(f => f.ChannelId == _channelIds["company"]));
        Assert.Empty(customerA.Conversations.Where(c => c.ChannelId == _channelIds["b"]));
        Assert.NotNull(customerA.Channels.FirstOrDefault(c => c.Id == _channelIds["a"]));
    }

    [Fact]
    public void IncludeThroughAnOwnChannel_DoesNotPullOtherOwnersRows()
    {
        using var db = Open(new TenantIdentity(TenantScope.Customer, CustomerA));
        var channels = db.Channels.Include(c => c.Conversations).ThenInclude(c => c.Messages).ToList();

        var channel = Assert.Single(channels);
        Assert.Equal("a", channel.Name);
        Assert.Single(channel.Conversations);
        Assert.Single(channel.Conversations.Single().Messages);
    }
}
