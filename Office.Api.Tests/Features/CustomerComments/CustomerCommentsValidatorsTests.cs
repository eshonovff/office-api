using Office.Api.Features.CustomerComments;

namespace Office.Api.Tests.Features.CustomerComments;

public class CustomerCommentsValidatorsTests
{
    [Theory]
    [InlineData("17977626660078004", true)]
    [InlineData("179776_266600", true)]
    [InlineData("", false)]
    [InlineData("../me/messages", false)]
    [InlineData("17977626660078004/comments", false)]
    [InlineData("17977626660078004?fields=id", false)]
    public void MediaId_IsDigitsOnly_SoItCanNeverBendAGraphUrl(string mediaId, bool valid)
    {
        var result = new CommentsOfPostRequestValidator().Validate(new CommentsOfPostRequest(Guid.NewGuid(), mediaId));
        Assert.Equal(valid, result.IsValid);
    }

    [Fact]
    public void ChannelId_IsRequired()
    {
        Assert.False(new CommentsOfPostRequestValidator().Validate(new CommentsOfPostRequest(Guid.Empty, "1")).IsValid);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("Ташаккур!", true)]
    public void Text_MustSaySomething(string text, bool valid) =>
        Assert.Equal(valid, new CommentTextRequestValidator().Validate(new CommentTextRequest(text)).IsValid);

    [Fact]
    public void Text_UpToInstagramsLimit()
    {
        var validator = new CommentTextRequestValidator();
        Assert.True(validator.Validate(new CommentTextRequest(new string('a', 1000))).IsValid);
        Assert.False(validator.Validate(new CommentTextRequest(new string('a', 1001))).IsValid);
    }
}
