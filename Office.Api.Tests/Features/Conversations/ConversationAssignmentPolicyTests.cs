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
}
