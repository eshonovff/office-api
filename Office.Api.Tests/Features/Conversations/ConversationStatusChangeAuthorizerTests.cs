using Office.Api.Data.Entities;
using Office.Api.Features.Conversations;

namespace Office.Api.Tests.Features.Conversations;

public class ConversationStatusChangeAuthorizerTests
{
    [Theory]
    [InlineData(ConversationStatus.New, false)]
    [InlineData(ConversationStatus.InProgress, false)]
    [InlineData(ConversationStatus.Waiting, false)]
    [InlineData(ConversationStatus.Closed, true)]
    public void RequiresClosePermission_MatchesOnlyClosedStatus(ConversationStatus status, bool expected)
    {
        Assert.Equal(expected, ConversationStatusChangeAuthorizer.RequiresClosePermission(status));
    }
}
