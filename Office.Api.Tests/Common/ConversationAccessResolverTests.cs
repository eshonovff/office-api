using Office.Api.Common;

namespace Office.Api.Tests.Common;

public class ConversationAccessResolverTests
{
    [Fact]
    public void CanAccess_CanSeeAllChannels_NotMember_NoOnlyAssigned_ReturnsTrue()
    {
        // Owner/Admin — узви channel_members набошад ҳам, мебинад.
        Assert.True(ConversationAccessResolver.CanAccess(
            canSeeAllChannels: true, isChannelMember: false, onlyAssigned: false, isAssignedToUser: false));
    }

    [Fact]
    public void CanAccess_NotCanSeeAll_NotMember_ReturnsFalse()
    {
        Assert.False(ConversationAccessResolver.CanAccess(
            canSeeAllChannels: false, isChannelMember: false, onlyAssigned: false, isAssignedToUser: false));
    }

    [Fact]
    public void CanAccess_NotCanSeeAll_IsMember_NoOnlyAssigned_ReturnsTrue()
    {
        Assert.True(ConversationAccessResolver.CanAccess(
            canSeeAllChannels: false, isChannelMember: true, onlyAssigned: false, isAssignedToUser: false));
    }

    [Fact]
    public void CanAccess_IsMember_OnlyAssigned_NotAssignedToUser_ReturnsFalse()
    {
        // Оператор узви канал аст, вале ин чат ба вай таъин нашудааст ва only_assigned фаъол аст.
        Assert.False(ConversationAccessResolver.CanAccess(
            canSeeAllChannels: false, isChannelMember: true, onlyAssigned: true, isAssignedToUser: false));
    }

    [Fact]
    public void CanAccess_IsMember_OnlyAssigned_AssignedToUser_ReturnsTrue()
    {
        Assert.True(ConversationAccessResolver.CanAccess(
            canSeeAllChannels: false, isChannelMember: true, onlyAssigned: true, isAssignedToUser: true));
    }

    [Fact]
    public void CanAccess_CanSeeAll_OnlyAssigned_NotAssignedToUser_ReturnsFalse()
    {
        // only_assigned новобаста аз канал-байпасс амал мекунад — санадоки алоҳидаи мустақил.
        Assert.False(ConversationAccessResolver.CanAccess(
            canSeeAllChannels: true, isChannelMember: false, onlyAssigned: true, isAssignedToUser: false));
    }

    [Fact]
    public void CanAccess_CanSeeAll_OnlyAssigned_AssignedToUser_ReturnsTrue()
    {
        Assert.True(ConversationAccessResolver.CanAccess(
            canSeeAllChannels: true, isChannelMember: false, onlyAssigned: true, isAssignedToUser: true));
    }

    // ChannelAccessGuard.CanUserBeAssignedToChannelAsync доир ба ҳамин формула сохта шудааст
    // (isAssignedToUser:true — "баъд аз таъин", canSeeAllChannels ҳамеша false дар ин ҷо, чун
    // Owner/Admin аллакай ба isChannelMember дохил карда мешавад). Ду ҳолати зерин мустақиман
    // рафтори PATCH /conversations/{id}-ро ҳангоми таъини корманди берун аз канал месанҷанд.

    [Fact]
    public void CanAccess_TargetNotChannelMember_AssignedToUser_ReturnsFalse()
    {
        // Bug: корманде, ки узви канали ин чат нест, таъин мешуд ва баъд аз login чатро
        // намедид (таъиноти "орфан"). Ин ҳолат бояд PATCH-ро бо 409 рад кунад.
        Assert.False(ConversationAccessResolver.CanAccess(
            canSeeAllChannels: false, isChannelMember: false, onlyAssigned: false, isAssignedToUser: true));
    }

    [Fact]
    public void CanAccess_TargetIsChannelMember_AssignedToUser_ReturnsTrue()
    {
        Assert.True(ConversationAccessResolver.CanAccess(
            canSeeAllChannels: false, isChannelMember: true, onlyAssigned: false, isAssignedToUser: true));
    }
}
