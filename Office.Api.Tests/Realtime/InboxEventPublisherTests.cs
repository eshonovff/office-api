using Microsoft.EntityFrameworkCore;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Features.Conversations;
using Office.Api.Realtime;

namespace Office.Api.Tests.Realtime;

public class InboxEventPublisherTests
{
    private readonly FakeHubContext _hub = new();
    private readonly FakeHubContext<CustomerHub> _customerHub = new();

    private readonly AppDbContext _db =
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private InboxEventPublisher Publisher() => new(_hub, _customerHub, _db);

    private Guid AddChannel(Guid? owner)
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(), Type = ChannelType.Instagram, Name = "ig", ExternalId = Guid.NewGuid().ToString(), CustomerId = owner,
        };
        _db.Channels.Add(channel);
        _db.SaveChanges();
        return channel.Id;
    }

    [Fact]
    public async Task MessageReceivedAsync_NoAssignee_SendsOnlyToChannelGroup()
    {
        var channelId = Guid.NewGuid();

        await Publisher().MessageReceivedAsync(channelId, null, new { text = "hi" }, CancellationToken.None);

        var group = Assert.Single(_hub.ClientsImpl.GroupProxies);
        Assert.Equal(InboxHub.ChannelGroupName(channelId), group.Key);
        Assert.Equal("MessageReceived", group.Value.Sent.Single().Method);
    }

    [Fact]
    public async Task ConversationAssignedAsync_WithAssignee_SendsToChannelAndUserGroups()
    {
        var channelId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await Publisher().ConversationAssignedAsync(channelId, userId, new { }, CancellationToken.None);

        Assert.Equal(2, _hub.ClientsImpl.GroupProxies.Count);
        Assert.Contains(InboxHub.ChannelGroupName(channelId), _hub.ClientsImpl.GroupProxies.Keys);
        Assert.Contains(InboxHub.UserGroupName(userId), _hub.ClientsImpl.GroupProxies.Keys);

        foreach (var proxy in _hub.ClientsImpl.GroupProxies.Values)
            Assert.Equal("ConversationAssigned", proxy.Sent.Single().Method);
    }

    [Fact]
    public async Task ConversationStatusChangedAsync_NoAssignee_DoesNotTargetAnyUserGroup()
    {
        await Publisher().ConversationStatusChangedAsync(Guid.NewGuid(), null, new { }, CancellationToken.None);

        Assert.All(_hub.ClientsImpl.GroupProxies.Keys, key => Assert.DoesNotContain("user:", key));
    }

    [Fact]
    public async Task MessageSentAsync_PassesPayloadThrough()
    {
        var channelId = Guid.NewGuid();
        var payload = new { messageId = Guid.NewGuid() };

        await Publisher().MessageSentAsync(channelId, null, payload, CancellationToken.None);

        var sent = _hub.ClientsImpl.GroupProxies[InboxHub.ChannelGroupName(channelId)].Sent.Single();
        Assert.Same(payload, sent.Args[0]);
    }

    [Fact]
    public async Task CompanyChannel_NothingGoesToAnyCustomer()
    {
        var channelId = AddChannel(owner: null);

        await Publisher().MessageReceivedAsync(channelId, null, new { }, CancellationToken.None);

        Assert.Empty(_customerHub.ClientsImpl.GroupProxies);
    }

    [Fact]
    public async Task CustomerChannel_OnlyItsOwnerGetsABareChatUpdated()
    {
        var owner = Guid.NewGuid();
        AddChannel(owner: Guid.NewGuid()); // someone else's channel exists too
        var channelId = AddChannel(owner);
        var conversationId = Guid.NewGuid();
        var message = MessageDto.FromEntity(new Message
        {
            Id = Guid.NewGuid(), ConversationId = conversationId, Direction = MessageDirection.Inbound,
            Type = MessageType.Text, Body = "secret text", CreatedAt = DateTimeOffset.UtcNow,
        });

        await Publisher().MessageReceivedAsync(channelId, null, message, CancellationToken.None);

        var group = Assert.Single(_customerHub.ClientsImpl.GroupProxies);
        Assert.Equal(CustomerHub.CustomerGroupName(owner), group.Key);
        var sent = group.Value.Sent.Single();
        Assert.Equal(CustomerHub.ChatUpdatedEvent, sent.Method);
        // Only the id travels — never the staff-shaped payload (body, sender, media paths).
        Assert.Equal($"{{ conversationId = {conversationId} }}", sent.Args[0]!.ToString());
    }
}
