using Office.Api.Common;

namespace Office.Api.Tests.Common;

public class ChannelListAccessResolverTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Resolve_CanSeeAllChannels_ReturnsAllJoinableRegardlessOfOnlyAssigned(bool onlyAssigned)
    {
        var policy = ChannelListAccessResolver.Resolve(canSeeAllChannels: true, onlyAssigned);

        Assert.Equal(ChannelListScope.All, policy.Scope);
        Assert.True(policy.Joinable);
    }

    [Fact]
    public void Resolve_NotAdminAndNotOnlyAssigned_ReturnsMembersOnlyJoinable()
    {
        var policy = ChannelListAccessResolver.Resolve(canSeeAllChannels: false, onlyAssigned: false);

        Assert.Equal(ChannelListScope.MembersOnly, policy.Scope);
        Assert.True(policy.Joinable);
    }

    [Fact]
    public void Resolve_NotAdminAndOnlyAssigned_ReturnsAssignedOnlyNotJoinable()
    {
        // only_assigned корбар ба ягон гурӯҳи канали пурра дастрасӣ надорад — паёмҳои
        // таъиншудаашро тавассути гурӯҳи user:{id} мегирад, на channel:{id} (ҳамон алгуи
        // InboxHub.JoinChannel-и HasAccessAsync бо assignedTo:null) — бинобар ин на joinable,
        // вале ба ин маъно нест, ки рӯйхаташ бояд холӣ бошад (ниг. масъалаи №4-и PROGRESS.md).
        var policy = ChannelListAccessResolver.Resolve(canSeeAllChannels: false, onlyAssigned: true);

        Assert.Equal(ChannelListScope.AssignedOnly, policy.Scope);
        Assert.False(policy.Joinable);
    }

    // CanAccessChannel — санҷиши як канали мушаххас (GET /{id}, /{id}/whatsapp-templates), на филтри рӯйхат.

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CanAccessChannel_ScopeAll_AlwaysAllowed(bool isInScope)
    {
        Assert.True(ChannelListAccessResolver.CanAccessChannel(ChannelListScope.All, isInScope));
    }

    [Fact]
    public void CanAccessChannel_ScopeMembersOnly_IsMember_Allowed()
    {
        Assert.True(ChannelListAccessResolver.CanAccessChannel(ChannelListScope.MembersOnly, isInScope: true));
    }

    [Fact]
    public void CanAccessChannel_ScopeMembersOnly_NotMember_Denied()
    {
        Assert.False(ChannelListAccessResolver.CanAccessChannel(ChannelListScope.MembersOnly, isInScope: false));
    }

    [Fact]
    public void CanAccessChannel_ScopeAssignedOnly_HasAssignedConversationInChannel_Allowed()
    {
        Assert.True(ChannelListAccessResolver.CanAccessChannel(ChannelListScope.AssignedOnly, isInScope: true));
    }

    [Fact]
    public void CanAccessChannel_ScopeAssignedOnly_NoAssignedConversationInChannel_Denied()
    {
        Assert.False(ChannelListAccessResolver.CanAccessChannel(ChannelListScope.AssignedOnly, isInScope: false));
    }
}
