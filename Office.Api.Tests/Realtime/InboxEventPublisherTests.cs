using Office.Api.Realtime;

namespace Office.Api.Tests.Realtime;

public class InboxEventPublisherTests
{
    [Fact]
    public async Task MessageReceivedAsync_NoAssignee_SendsOnlyToChannelGroup()
    {
        var hubContext = new FakeHubContext();
        var publisher = new InboxEventPublisher(hubContext);
        var channelId = Guid.NewGuid();

        await publisher.MessageReceivedAsync(channelId, null, new { text = "hi" }, CancellationToken.None);

        var group = Assert.Single(hubContext.ClientsImpl.GroupProxies);
        Assert.Equal(InboxHub.ChannelGroupName(channelId), group.Key);
        Assert.Equal("MessageReceived", group.Value.Sent.Single().Method);
    }

    [Fact]
    public async Task ConversationAssignedAsync_WithAssignee_SendsToChannelAndUserGroups()
    {
        var hubContext = new FakeHubContext();
        var publisher = new InboxEventPublisher(hubContext);
        var channelId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await publisher.ConversationAssignedAsync(channelId, userId, new { }, CancellationToken.None);

        Assert.Equal(2, hubContext.ClientsImpl.GroupProxies.Count);
        Assert.Contains(InboxHub.ChannelGroupName(channelId), hubContext.ClientsImpl.GroupProxies.Keys);
        Assert.Contains(InboxHub.UserGroupName(userId), hubContext.ClientsImpl.GroupProxies.Keys);

        foreach (var proxy in hubContext.ClientsImpl.GroupProxies.Values)
            Assert.Equal("ConversationAssigned", proxy.Sent.Single().Method);
    }

    [Fact]
    public async Task ConversationStatusChangedAsync_NoAssignee_DoesNotTargetAnyUserGroup()
    {
        var hubContext = new FakeHubContext();
        var publisher = new InboxEventPublisher(hubContext);
        var channelId = Guid.NewGuid();

        await publisher.ConversationStatusChangedAsync(channelId, null, new { }, CancellationToken.None);

        Assert.All(hubContext.ClientsImpl.GroupProxies.Keys, key => Assert.DoesNotContain("user:", key));
    }

    [Fact]
    public async Task MessageSentAsync_PassesPayloadThrough()
    {
        var hubContext = new FakeHubContext();
        var publisher = new InboxEventPublisher(hubContext);
        var channelId = Guid.NewGuid();
        var payload = new { messageId = Guid.NewGuid() };

        await publisher.MessageSentAsync(channelId, null, payload, CancellationToken.None);

        var sent = hubContext.ClientsImpl.GroupProxies[InboxHub.ChannelGroupName(channelId)].Sent.Single();
        Assert.Same(payload, sent.Args[0]);
    }
}
