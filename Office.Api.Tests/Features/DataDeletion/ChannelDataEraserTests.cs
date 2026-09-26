using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Features.DataDeletion;

namespace Office.Api.Tests.Features.DataDeletion;

/// <summary>One full channel tree per owner (company, мизоҷ A, мизоҷ B); erase one, count all.</summary>
public class ChannelDataEraserTests
{
    private static readonly Guid MizojA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid MizojB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private sealed class FixedTenant(TenantIdentity identity) : ITenantContext
    {
        public TenantIdentity Current => identity;
    }

    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly Dictionary<string, Guid> _channel = [];
    private readonly Dictionary<string, Seeded> _seeded = [];

    /// <summary>The ids each tree was seeded with — counted by these, a leftover can't hide.</summary>
    private sealed record Seeded(Guid ContactId, Guid RuleId, Guid FlowId, Guid SessionId, Guid BroadcastId);

    public ChannelDataEraserTests()
    {
        using var db = Open(null);
        var staff = new User { Id = Guid.NewGuid(), FullName = "S", Username = "s", PasswordHash = "x" };
        db.Users.Add(staff);
        db.Customers.AddRange(
            new Customer { Id = MizojA, Email = "a@example.com", FullName = "A" },
            new Customer { Id = MizojB, Email = "b@example.com", FullName = "B" });
        _channel["company"] = Seed(db, "company", null, staff.Id);
        _channel["a"] = Seed(db, "a", MizojA, staff.Id);
        _channel["b"] = Seed(db, "b", MizojB, staff.Id);
        db.SaveChanges();
    }

    private AppDbContext Open(TenantIdentity? tenant) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_databaseName).Options,
        tenant is null ? null : new FixedTenant(tenant.Value));

    private Guid Seed(AppDbContext db, string tag, Guid? owner, Guid staffId)
    {
        var now = DateTimeOffset.UtcNow;
        var channelId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();
        var flowId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        db.Channels.Add(new Channel { Id = channelId, Type = ChannelType.Instagram, Name = tag, ExternalId = tag, CustomerId = owner, CreatedAt = now });
        db.ChannelMembers.Add(new ChannelMember { ChannelId = channelId, UserId = staffId });
        db.Conversations.Add(new Conversation { Id = contactId, ChannelId = channelId, ExternalId = tag, CreatedAt = now });
        db.Messages.Add(new Message { Id = Guid.NewGuid(), ConversationId = contactId, CreatedAt = now });
        db.ConversationAssignmentEvents.Add(new ConversationAssignmentEvent { Id = Guid.NewGuid(), ConversationId = contactId, CreatedAt = now });
        db.ContactTags.Add(new ContactTag { ContactId = contactId, Tag = tag, CreatedAt = now });
        db.ContactVariables.Add(new ContactVariable { ContactId = contactId, Key = "k", Value = tag });
        db.AutomationRules.Add(new AutomationRule
        {
            Id = ruleId, ChannelId = channelId, Name = tag, TriggerType = "instagram_comment",
            TriggerConfigJson = "{}", ActionConfigJson = "{}", CreatedAt = now,
        });
        db.AutomationRuns.Add(new AutomationRun { Id = Guid.NewGuid(), RuleId = ruleId, TriggerExternalId = tag, ActorExternalId = tag, CreatedAt = now });
        db.Flows.Add(new Flow { Id = flowId, ChannelId = channelId, Name = tag, TriggerType = "instagram_dm", TriggerConfigJson = "{}", CreatedAt = now, UpdatedAt = now });
        db.FlowNodes.Add(new FlowNode { Id = nodeId, FlowId = flowId, Type = FlowNodeType.Message, ConfigJson = "{}" });
        db.FlowEdges.Add(new FlowEdge { Id = Guid.NewGuid(), FlowId = flowId, FromNodeId = nodeId, FromPort = "default", ToNodeId = nodeId });
        db.FlowSessions.Add(new FlowSession { Id = sessionId, FlowId = flowId, ContactId = contactId, CreatedAt = now });
        db.FlowSessionSteps.Add(new FlowSessionStep { Id = Guid.NewGuid(), SessionId = sessionId, NodeId = nodeId, CreatedAt = now });
        db.InstagramComments.Add(new InstagramComment
        {
            Id = Guid.NewGuid(), ChannelId = channelId, ExternalId = tag, MediaExternalId = tag,
            AuthorExternalId = tag, Text = tag, CommentedAt = now, ReceivedAt = now,
        });
        var broadcastId = Guid.NewGuid();
        db.Broadcasts.Add(new Broadcast { Id = broadcastId, ChannelId = channelId, Name = tag, Text = tag, ScheduledAt = now, CreatedAt = now });
        db.BroadcastRecipients.Add(new BroadcastRecipient { BroadcastId = broadcastId, ContactId = contactId });
        _seeded[tag] = new Seeded(contactId, ruleId, flowId, sessionId, broadcastId);
        return channelId;
    }

    /// <summary>
    /// Row counts in all 17 channel-owned tables — read with the tenant filters OFF: a filter that
    /// joins to the channel would hide rows left behind by a deleted channel (the in-memory
    /// provider has no foreign keys to cascade), and a leftover is exactly what this must catch.
    /// </summary>
    private int[] CountsFor(string tag)
    {
        using var db = Open(null);
        var channelId = _channel[tag];
        var ids = _seeded[tag];
        return
        [
            db.Channels.IgnoreQueryFilters().Count(c => c.Id == channelId),
            db.ChannelMembers.IgnoreQueryFilters().Count(m => m.ChannelId == channelId),
            db.Conversations.IgnoreQueryFilters().Count(c => c.Id == ids.ContactId),
            db.Messages.IgnoreQueryFilters().Count(m => m.ConversationId == ids.ContactId),
            db.ConversationAssignmentEvents.IgnoreQueryFilters().Count(e => e.ConversationId == ids.ContactId),
            db.ContactTags.IgnoreQueryFilters().Count(t => t.ContactId == ids.ContactId),
            db.ContactVariables.IgnoreQueryFilters().Count(v => v.ContactId == ids.ContactId),
            db.AutomationRules.IgnoreQueryFilters().Count(r => r.Id == ids.RuleId),
            db.AutomationRuns.IgnoreQueryFilters().Count(r => r.RuleId == ids.RuleId),
            db.Flows.IgnoreQueryFilters().Count(f => f.Id == ids.FlowId),
            db.FlowNodes.IgnoreQueryFilters().Count(n => n.FlowId == ids.FlowId),
            db.FlowEdges.IgnoreQueryFilters().Count(e => e.FlowId == ids.FlowId),
            db.FlowSessions.IgnoreQueryFilters().Count(x => x.Id == ids.SessionId),
            db.FlowSessionSteps.IgnoreQueryFilters().Count(x => x.SessionId == ids.SessionId),
            db.InstagramComments.IgnoreQueryFilters().Count(c => c.ChannelId == channelId),
            db.Broadcasts.IgnoreQueryFilters().Count(b => b.Id == ids.BroadcastId),
            db.BroadcastRecipients.IgnoreQueryFilters().Count(r => r.BroadcastId == ids.BroadcastId),
        ];
    }

    private static readonly int[] Full = Enumerable.Repeat(1, 17).ToArray();
    private static readonly int[] Gone = new int[17];

    [Fact]
    public async Task Erase_RemovesTheWholeTree_AndNothingElse()
    {
        await using (var db = Open(null))
            await ChannelDataEraser.EraseAsync(db, [_channel["a"]], CancellationToken.None);

        Assert.Equal(Gone, CountsFor("a"));
        Assert.Equal(Full, CountsFor("b"));
        Assert.Equal(Full, CountsFor("company"));
    }

    [Fact]
    public async Task Erase_InAnotherMizojsScope_CannotReachThisChannel()
    {
        // мизоҷ B passing A's channel id: the tenant filter hides A's rows, so nothing goes.
        await using (var db = Open(new TenantIdentity(TenantScope.Customer, MizojB)))
            await ChannelDataEraser.EraseAsync(db, [_channel["a"]], CancellationToken.None);

        Assert.Equal(Full, CountsFor("a"));
    }

    [Fact]
    public async Task Erase_IsIdempotent()
    {
        await using (var db = Open(null))
            await ChannelDataEraser.EraseAsync(db, [_channel["a"]], CancellationToken.None);
        await using (var db = Open(null))
            await ChannelDataEraser.EraseAsync(db, [_channel["a"]], CancellationToken.None);

        Assert.Equal(Gone, CountsFor("a"));
        Assert.Equal(Full, CountsFor("b"));
    }

    [Fact]
    public void DeleteMediaFolders_RemovesOnlyTheseChannelsFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "eraser-test-" + Guid.NewGuid());
        var mine = Path.Combine(root, "whatsapp-media", _channel["a"].ToString());
        var other = Path.Combine(root, "whatsapp-media", _channel["b"].ToString());
        Directory.CreateDirectory(mine);
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(mine, "x.jpg"), "x");
        File.WriteAllText(Path.Combine(other, "y.jpg"), "y");
        try
        {
            ChannelDataEraser.DeleteMediaFolders(root, [_channel["a"]]);

            Assert.False(Directory.Exists(mine));
            Assert.True(File.Exists(Path.Combine(other, "y.jpg")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
