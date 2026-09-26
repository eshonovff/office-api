using Office.Api.Channels.Automation;
using Office.Api.Features.Flows;

namespace Office.Api.Tests.Features.Flows;

/// <summary>
/// Phase 20: which trigger settings a flow may be saved with. The story types are new; the DM
/// "selected posts" trap (it never matched) and a missing keyword list (it broke every DM webhook of
/// the channel) are closed on the way.
/// </summary>
public class FlowTriggerValidatorTests
{
    private static AutomationTriggerConfig Config(
        string matchMode = "all", string[]? keywords = null, string postScope = "all", string[]? postIds = null) =>
        new(matchMode, keywords ?? [], postScope, postIds ?? []);

    private static bool CreateIsValid(string triggerType, AutomationTriggerConfig config) =>
        new CreateFlowRequestValidator().Validate(new CreateFlowRequest("Флоу", triggerType, config)).IsValid;

    private static bool UpdateIsValid(string triggerType, AutomationTriggerConfig config) =>
        new UpdateFlowRequestValidator().Validate(new UpdateFlowRequest("Флоу", triggerType, config)).IsValid;

    [Theory]
    [InlineData("instagram_comment")]
    [InlineData("instagram_dm")]
    [InlineData("instagram_story_reply")]
    [InlineData("instagram_story_mention")]
    public void EveryTriggerType_AcceptsAnyMessage(string triggerType)
    {
        Assert.True(CreateIsValid(triggerType, Config()));
        Assert.True(UpdateIsValid(triggerType, Config()));
    }

    [Theory]
    [InlineData("instagram_story")]
    [InlineData("whatsapp_dm")]
    [InlineData("")]
    public void AnUnknownTriggerType_IsRejected(string triggerType)
    {
        Assert.False(CreateIsValid(triggerType, Config()));
        Assert.False(UpdateIsValid(triggerType, Config()));
    }

    [Fact]
    public void StoryReply_TakesKeywordsAndSelectedStories()
    {
        Assert.True(CreateIsValid("instagram_story_reply", Config("keyword", ["нарх"], "selected", ["17900000000000000"])));
        Assert.True(UpdateIsValid("instagram_story_reply", Config("all", null, "selected", ["17900000000000000"])));
    }

    [Theory]
    [InlineData("instagram_dm")]
    [InlineData("instagram_story_mention")]
    public void SelectedPostsOrStories_OnlyWhereTheyExist(string triggerType)
    {
        Assert.False(CreateIsValid(triggerType, Config(postScope: "selected", postIds: ["17900000000000000"])));
        Assert.False(UpdateIsValid(triggerType, Config(postScope: "selected", postIds: ["17900000000000000"])));
    }

    [Fact]
    public void AStoryMention_HasNoText_SoNoKeywords()
    {
        Assert.False(CreateIsValid("instagram_story_mention", Config("keyword", ["конкурс"])));
        Assert.False(UpdateIsValid("instagram_story_mention", Config("keyword", ["конкурс"])));
    }

    [Fact]
    public void TheTriggerSettingsThemselves_AreChecked()
    {
        // A missing keyword list used to be saved and then threw on every message of the channel.
        Assert.False(CreateIsValid("instagram_dm", new AutomationTriggerConfig("keyword", null!, "all", [])));
        Assert.False(CreateIsValid("instagram_dm", Config("keyword")));
        Assert.False(UpdateIsValid("instagram_story_reply", Config("sometimes")));
        Assert.False(UpdateIsValid("instagram_story_reply", Config(postScope: "selected")));
        Assert.False(CreateIsValid("instagram_story_reply", Config(postScope: "selected", postIds: ["../../etc"])));
    }
}
