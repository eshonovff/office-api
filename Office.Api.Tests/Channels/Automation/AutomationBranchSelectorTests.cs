using Office.Api.Channels.Automation;
using Office.Api.Data.Entities;

namespace Office.Api.Tests.Channels.Automation;

public class AutomationBranchSelectorTests
{
    private static readonly AutomationReplyAction OnMatch = new(["salom onMatch"], "dm onMatch", null);
    private static readonly AutomationReplyAction OnNotFollowing = new(["salom onNotFollowing"], "dm onNotFollowing", null);

    [Fact]
    public void Select_Following_ReturnsOnMatch()
    {
        var config = new AutomationActionConfig(OnMatch, OnNotFollowing);

        Assert.Same(OnMatch, AutomationBranchSelector.Select(config, FollowCheckResult.Following));
    }

    [Fact]
    public void Select_NotFollowing_WithOnNotFollowingConfigured_ReturnsOnNotFollowing()
    {
        var config = new AutomationActionConfig(OnMatch, OnNotFollowing);

        Assert.Same(OnNotFollowing, AutomationBranchSelector.Select(config, FollowCheckResult.NotFollowing));
    }

    [Fact]
    public void Select_Unknown_FailsOpenToOnMatch()
    {
        var config = new AutomationActionConfig(OnMatch, OnNotFollowing);

        Assert.Same(OnMatch, AutomationBranchSelector.Select(config, FollowCheckResult.Unknown));
    }

    [Fact]
    public void Select_NullFollowCheckResult_RequiresFollowWasFalse_ReturnsOnMatch()
    {
        var config = new AutomationActionConfig(OnMatch, OnNotFollowing);

        Assert.Same(OnMatch, AutomationBranchSelector.Select(config, null));
    }

    [Fact]
    public void Select_NotFollowing_WithoutOnNotFollowingConfigured_FallsBackToOnMatch()
    {
        var config = new AutomationActionConfig(OnMatch, OnNotFollowing: null);

        Assert.Same(OnMatch, AutomationBranchSelector.Select(config, FollowCheckResult.NotFollowing));
    }
}
