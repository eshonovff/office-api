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
}
