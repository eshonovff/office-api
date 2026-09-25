using Office.Api.Channels.Automation;

namespace Office.Api.Tests.Channels.Automation;

public class CommentAutomationMatcherTests
{
    private static AutomationTriggerConfig Keyword(params string[] keywords) =>
        new(AutomationTriggerConfig.MatchModeKeyword, keywords, AutomationTriggerConfig.PostScopeAll, []);

    [Fact]
    public void Match_KeywordPresent_ReturnsMatchedWithKeyword()
    {
        var result = CommentAutomationMatcher.Match(Keyword("нарх"), "Нархаш чанд аст?", null);

        Assert.True(result.Matched);
        Assert.Equal("нарх", result.MatchedKeyword);
    }

    [Fact]
    public void Match_KeywordDifferentCase_IsCaseInsensitive()
    {
        var result = CommentAutomationMatcher.Match(Keyword("PRICE"), "what is the price?", null);

        Assert.True(result.Matched);
    }

    [Fact]
    public void Match_EmojiOnlyCommentText_NoKeywordMatch_ReturnsFalse()
    {
        var result = CommentAutomationMatcher.Match(Keyword("нарх"), "🔥👏", null);

        Assert.False(result.Matched);
    }

    [Fact]
    public void Match_EmptyCommentText_ReturnsFalse()
    {
        var result = CommentAutomationMatcher.Match(Keyword("нарх"), "", null);

        Assert.False(result.Matched);
    }

    [Fact]
    public void Match_MatchModeAll_MatchesEverythingRegardlessOfKeywords()
    {
        var config = new AutomationTriggerConfig(AutomationTriggerConfig.MatchModeAll, [], AutomationTriggerConfig.PostScopeAll, []);

        var result = CommentAutomationMatcher.Match(config, "🔥👏", null);

        Assert.True(result.Matched);
        Assert.Null(result.MatchedKeyword);
    }

    [Fact]
    public void Match_PostScopeSelected_MediaNotInList_ReturnsFalse()
    {
        var config = new AutomationTriggerConfig(AutomationTriggerConfig.MatchModeAll, [], AutomationTriggerConfig.PostScopeSelected, ["media-1"]);

        var result = CommentAutomationMatcher.Match(config, "hello", "media-2");

        Assert.False(result.Matched);
    }

    [Fact]
    public void Match_PostScopeSelected_MediaInList_ChecksKeywordNext()
    {
        var config = new AutomationTriggerConfig(AutomationTriggerConfig.MatchModeKeyword, ["нарх"], AutomationTriggerConfig.PostScopeSelected, ["media-1"]);

        var result = CommentAutomationMatcher.Match(config, "нархаш чанд?", "media-1");

        Assert.True(result.Matched);
    }

    [Fact]
    public void Match_PostScopeSelected_NullMediaId_ReturnsFalse()
    {
        var config = new AutomationTriggerConfig(AutomationTriggerConfig.MatchModeAll, [], AutomationTriggerConfig.PostScopeSelected, ["media-1"]);

        var result = CommentAutomationMatcher.Match(config, "hello", null);

        Assert.False(result.Matched);
    }

    [Fact]
    public void Match_MultipleKeywords_FirstMatchingOneIsReturned()
    {
        var result = CommentAutomationMatcher.Match(Keyword("нарх", "салом"), "Салом, чӣ хел?", null);

        Assert.True(result.Matched);
        Assert.Equal("салом", result.MatchedKeyword);
    }
}
