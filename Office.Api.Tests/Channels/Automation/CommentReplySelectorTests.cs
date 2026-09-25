using Office.Api.Channels.Automation;

namespace Office.Api.Tests.Channels.Automation;

public class CommentReplySelectorTests
{
    [Theory]
    [InlineData(0, "a")]
    [InlineData(1, "b")]
    [InlineData(2, "c")]
    [InlineData(3, "a")]
    [InlineData(4, "b")]
    public void Select_CyclesThroughRepliesInOrder(int priorRunCount, string expected)
    {
        var result = CommentReplySelector.Select(["a", "b", "c"], priorRunCount);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Select_SingleReply_AlwaysReturnsIt()
    {
        Assert.Equal("only", CommentReplySelector.Select(["only"], 5));
    }
}
