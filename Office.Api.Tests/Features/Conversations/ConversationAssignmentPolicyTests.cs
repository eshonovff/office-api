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
}
