using Office.Api.Common;

namespace Office.Api.Tests.Common;

public class ChannelListAccessResolverTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Resolve_CanSeeAllChannels_ReturnsAllRegardlessOfOnlyAssigned(bool onlyAssigned)
    {
        Assert.Equal(ChannelListScope.All, ChannelListAccessResolver.Resolve(canSeeAllChannels: true, onlyAssigned));
    }

    [Fact]
    public void Resolve_NotAdminAndNotOnlyAssigned_ReturnsMembersOnly()
    {
        Assert.Equal(
            ChannelListScope.MembersOnly,
            ChannelListAccessResolver.Resolve(canSeeAllChannels: false, onlyAssigned: false));
    }

    [Fact]
    public void Resolve_NotAdminAndOnlyAssigned_ReturnsNone()
    {
        // only_assigned корбар ба ягон гурӯҳи канали пурра дастрасӣ надорад — паёмҳои
        // таъиншудаашро тавассути user:{id} мегирад, на channel:{id} (ҳамон алгуи
        // InboxHub.JoinChannel-и HasAccessAsync бо assignedTo:null).
        Assert.Equal(
            ChannelListScope.None,
            ChannelListAccessResolver.Resolve(canSeeAllChannels: false, onlyAssigned: true));
    }

    // CanAccessChannel — санҷиши як канали мушаххас (GET /{id}, /{id}/whatsapp-templates), на филтри рӯйхат.

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CanAccessChannel_ScopeAll_AlwaysAllowed(bool isChannelMember)
    {
        Assert.True(ChannelListAccessResolver.CanAccessChannel(ChannelListScope.All, isChannelMember));
    }

    [Fact]
    public void CanAccessChannel_ScopeMembersOnly_IsMember_Allowed()
    {
        Assert.True(ChannelListAccessResolver.CanAccessChannel(ChannelListScope.MembersOnly, isChannelMember: true));
    }

    [Fact]
    public void CanAccessChannel_ScopeMembersOnly_NotMember_Denied()
    {
        Assert.False(ChannelListAccessResolver.CanAccessChannel(ChannelListScope.MembersOnly, isChannelMember: false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CanAccessChannel_ScopeNone_AlwaysDenied(bool isChannelMember)
    {
        // only_assigned корбар — ҳатто агар (назариявӣ) узви канал бошад ҳам, то фазаи 4-и
        // GET /channels/{id} дастрасӣ надорад (ниг. масъалаи №7-и PROGRESS.md).
        Assert.False(ChannelListAccessResolver.CanAccessChannel(ChannelListScope.None, isChannelMember));
    }
}
