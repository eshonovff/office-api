using Office.Api.Data.Entities;
using Office.Api.Features.Conversations;

namespace Office.Api.Tests.Features.Conversations;

public class ConversationAssignmentPolicyTests
{
    [Fact]
    public void ShouldClaimOnReply_Unassigned_ReturnsTrue()
    {
        Assert.True(ConversationAssignmentPolicy.ShouldClaimOnReply(currentAssignedTo: null));
    }

    [Fact]
    public void ShouldClaimOnReply_AlreadyAssigned_ReturnsFalse()
    {
        // Ҷавоби дуюм ба чати аллакай таъиншуда набояд таъинотро иваз кунад.
        Assert.False(ConversationAssignmentPolicy.ShouldClaimOnReply(currentAssignedTo: Guid.NewGuid()));
    }

    [Fact]
    public void CanSend_Unassigned_AnyoneCanSend()
    {
        var userId = Guid.NewGuid();
        Assert.True(ConversationAssignmentPolicy.CanSend(isOwnerOrAdmin: false, assignedTo: null, userId));
    }

    [Fact]
    public void CanSend_AssignedToSelf_ReturnsTrue()
    {
        var userId = Guid.NewGuid();
        Assert.True(ConversationAssignmentPolicy.CanSend(isOwnerOrAdmin: false, assignedTo: userId, userId));
    }

    [Fact]
    public void CanSend_AssignedToSomeoneElse_NotOwnerOrAdmin_ReturnsFalse()
    {
        // Ин формула — асли "read-only барои ҳама, ба ғайр аз таъиншуда".
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        Assert.False(ConversationAssignmentPolicy.CanSend(isOwnerOrAdmin: false, assignedTo: otherUserId, userId));
    }

    [Fact]
    public void CanSend_AssignedToSomeoneElse_OwnerOrAdmin_ReturnsTrue()
    {
        // Owner/Admin ҳамеша метавонанд бинависанд — на танҳо гирифтан.
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        Assert.True(ConversationAssignmentPolicy.CanSend(isOwnerOrAdmin: true, assignedTo: otherUserId, userId));
    }

    private static readonly DateTimeOffset Now = new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan ThreeHours = TimeSpan.FromHours(3);

    [Fact]
    public void ShouldAutoRelease_ClosedConversation_NeverReleases()
    {
        // Чати пӯшида фаъол нест — новобаста аз он ки таъиншуда чанд вақт хомӯш аст.
        Assert.False(ConversationAssignmentPolicy.ShouldAutoRelease(
            ConversationStatus.Closed, assignedAt: Now.AddDays(-10), lastActivityByAssignee: null, Now, ThreeHours));
    }

    [Fact]
    public void ShouldAutoRelease_AssigneeNeverSent_UsesAssignedAtAsBaseline_StillWithinThreshold_ReturnsFalse()
    {
        Assert.False(ConversationAssignmentPolicy.ShouldAutoRelease(
            ConversationStatus.New, assignedAt: Now.AddHours(-2), lastActivityByAssignee: null, Now, ThreeHours));
    }

    [Fact]
    public void ShouldAutoRelease_AssigneeNeverSent_UsesAssignedAtAsBaseline_PastThreshold_ReturnsTrue()
    {
        Assert.True(ConversationAssignmentPolicy.ShouldAutoRelease(
            ConversationStatus.New, assignedAt: Now.AddHours(-4), lastActivityByAssignee: null, Now, ThreeHours));
    }

    [Fact]
    public void ShouldAutoRelease_RecentActivityByAssignee_ReturnsFalseEvenThoughAssignedLongAgo()
    {
        // Охирин фаъолияти таъиншуда нуқтаи сар аст, на лаҳзаи таъинот.
        Assert.False(ConversationAssignmentPolicy.ShouldAutoRelease(
            ConversationStatus.InProgress,
            assignedAt: Now.AddDays(-1),
            lastActivityByAssignee: Now.AddMinutes(-30),
            Now, ThreeHours));
    }

    [Fact]
    public void ShouldAutoRelease_StaleActivityByAssignee_PastThreshold_ReturnsTrue()
    {
        Assert.True(ConversationAssignmentPolicy.ShouldAutoRelease(
            ConversationStatus.InProgress,
            assignedAt: Now.AddDays(-1),
            lastActivityByAssignee: Now.AddHours(-5),
            Now, ThreeHours));
    }

    [Fact]
    public void ShouldAutoRelease_ExactlyAtThreshold_ReturnsTrue()
    {
        // >= — ҳудуд худаш аллакай release мешавад, на танҳо аз он гузашта.
        Assert.True(ConversationAssignmentPolicy.ShouldAutoRelease(
            ConversationStatus.New, assignedAt: Now.Add(-ThreeHours), lastActivityByAssignee: null, Now, ThreeHours));
    }
}
