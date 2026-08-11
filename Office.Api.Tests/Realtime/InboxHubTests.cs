using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Office.Api.Common;
using Office.Api.Data.Entities;
using Office.Api.Realtime;

namespace Office.Api.Tests.Realtime;

public class InboxHubTests
{
    private sealed class FakeChannelAccessGuard(bool result) : IChannelAccessGuard
    {
        public bool AssignedToWasCaptured { get; private set; }
        public Guid? LastAssignedTo { get; private set; }

        public Task<IQueryable<Conversation>> ApplyAccessFilterAsync(
            IQueryable<Conversation> query, ClaimsPrincipal principal, CancellationToken ct) => Task.FromResult(query);

        public Task<(IQueryable<Channel> Query, bool Joinable)> ApplyChannelAccessFilterAsync(
            IQueryable<Channel> query, ClaimsPrincipal principal, CancellationToken ct) =>
            Task.FromResult((query, true));

        public Task<bool> CanAccessChannelAsync(ClaimsPrincipal principal, Guid channelId, CancellationToken ct) =>
            Task.FromResult(result);

        public Task<bool> HasAccessAsync(ClaimsPrincipal principal, Guid channelId, Guid? assignedTo, CancellationToken ct)
        {
            AssignedToWasCaptured = true;
            LastAssignedTo = assignedTo;
            return Task.FromResult(result);
        }
    }

    private static InboxHub CreateHub(IChannelAccessGuard access, out FakeHubCallerContext context, out FakeGroupManager groups)
    {
        context = new FakeHubCallerContext { UserOverride = new ClaimsPrincipal(new ClaimsIdentity()) };
        groups = new FakeGroupManager();

        return new InboxHub(access) { Context = context, Groups = groups };
    }

    [Fact]
    public async Task JoinChannel_MemberOfChannel_JoinsGroup()
    {
        var channelId = Guid.NewGuid();
        var hub = CreateHub(new FakeChannelAccessGuard(result: true), out var context, out var groups);

        await hub.JoinChannel(channelId);

        var added = Assert.Single(groups.Added);
        Assert.Equal(context.ConnectionId, added.ConnectionId);
        Assert.Equal(InboxHub.ChannelGroupName(channelId), added.GroupName);
    }

    [Fact]
    public async Task JoinChannel_NotAMember_ThrowsAndNeverJoinsGroup()
    {
        var hub = CreateHub(new FakeChannelAccessGuard(result: false), out _, out var groups);

        await Assert.ThrowsAsync<HubException>(() => hub.JoinChannel(Guid.NewGuid()));

        Assert.Empty(groups.Added);
    }

    [Fact]
    public async Task JoinChannel_ChecksAccessWithoutASpecificAssignee()
    {
        // channel:{id} тамоми сӯҳбатҳои каналро мебарорад, на якеро — пас "assigned to me"
        // ба ин ҷо намеғунҷад. Агар ягон рӯз ин ба Context.User.GetUserId() иваз шавад,
        // корбари only_assigned метавонад бенатиҷа ба тамоми ҷараёни канал ҳамроҳ шавад.
        var access = new FakeChannelAccessGuard(result: true);
        var hub = CreateHub(access, out _, out _);

        await hub.JoinChannel(Guid.NewGuid());

        Assert.True(access.AssignedToWasCaptured);
        Assert.Null(access.LastAssignedTo);
    }
}
