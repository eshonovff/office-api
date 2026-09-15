using Office.Api.Channels.Flows;

namespace Office.Api.Tests.Channels.Flows;

public class ConditionEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 14, 30, 0, TimeSpan.Zero); // Tuesday

    private static ConditionContext Context(
        Dictionary<string, string>? variables = null, HashSet<string>? tags = null, bool? isFollowing = null, DateTimeOffset? now = null) =>
        new(variables ?? [], tags ?? [], isFollowing, now ?? Now);

    [Fact]
    public void Evaluate_NoRules_ReturnsTrue()
    {
        Assert.True(ConditionEvaluator.Evaluate(new ConditionNodeConfig(ConditionNodeConfig.MatchAll, []), Context()));
    }

    [Fact]
    public void Evaluate_Subscription_TrueWhenFollowing()
    {
        var config = new ConditionNodeConfig(ConditionNodeConfig.MatchAll, [new ConditionRule(ConditionRule.FieldSubscription, "equals", "")]);

        Assert.True(ConditionEvaluator.Evaluate(config, Context(isFollowing: true)));
        Assert.False(ConditionEvaluator.Evaluate(config, Context(isFollowing: false)));
        Assert.False(ConditionEvaluator.Evaluate(config, Context(isFollowing: null)));
    }

    [Fact]
    public void Evaluate_Tags_Has()
    {
        var config = new ConditionNodeConfig(ConditionNodeConfig.MatchAll, [new ConditionRule(ConditionRule.FieldTags, ConditionEvaluator.OpHas, "vip")]);

        Assert.True(ConditionEvaluator.Evaluate(config, Context(tags: ["vip", "lead"])));
        Assert.False(ConditionEvaluator.Evaluate(config, Context(tags: ["lead"])));
    }

    [Fact]
    public void Evaluate_Tags_NotHas()
    {
        var config = new ConditionNodeConfig(ConditionNodeConfig.MatchAll, [new ConditionRule(ConditionRule.FieldTags, ConditionEvaluator.OpNotHas, "vip")]);

        Assert.True(ConditionEvaluator.Evaluate(config, Context(tags: ["lead"])));
        Assert.False(ConditionEvaluator.Evaluate(config, Context(tags: ["vip"])));
    }

    [Fact]
    public void Evaluate_Variable_Equals()
    {
        var config = new ConditionNodeConfig(
            ConditionNodeConfig.MatchAll, [new ConditionRule("city", ConditionEvaluator.OpEquals, "Душанбе")]);

        Assert.True(ConditionEvaluator.Evaluate(config, Context(variables: new() { ["city"] = "Душанбе" })));
        Assert.False(ConditionEvaluator.Evaluate(config, Context(variables: new() { ["city"] = "Хуҷанд" })));
    }

    [Fact]
    public void Evaluate_Variable_Missing_TreatedAsNotEqual()
    {
        var config = new ConditionNodeConfig(ConditionNodeConfig.MatchAll, [new ConditionRule("city", ConditionEvaluator.OpEquals, "Душанбе")]);

        Assert.False(ConditionEvaluator.Evaluate(config, Context()));
    }

    [Fact]
    public void Evaluate_Variable_Contains()
    {
        var config = new ConditionNodeConfig(
            ConditionNodeConfig.MatchAll, [new ConditionRule("message", ConditionEvaluator.OpContains, "нарх")]);

        Assert.True(ConditionEvaluator.Evaluate(config, Context(variables: new() { ["message"] = "нархаш чанд?" })));
    }

    [Fact]
    public void Evaluate_Weekday_Equals()
    {
        var config = new ConditionNodeConfig(ConditionNodeConfig.MatchAll, [new ConditionRule(ConditionRule.FieldWeekday, ConditionEvaluator.OpEquals, "Tuesday")]);

        Assert.True(ConditionEvaluator.Evaluate(config, Context()));
    }

    [Fact]
    public void Evaluate_Time_After()
    {
        var config = new ConditionNodeConfig(ConditionNodeConfig.MatchAll, [new ConditionRule(ConditionRule.FieldTime, ConditionEvaluator.OpAfter, "09:00")]);

        Assert.True(ConditionEvaluator.Evaluate(config, Context())); // 14:30 >= 09:00
    }

    [Fact]
    public void Evaluate_Time_Before()
    {
        var config = new ConditionNodeConfig(ConditionNodeConfig.MatchAll, [new ConditionRule(ConditionRule.FieldTime, ConditionEvaluator.OpBefore, "09:00")]);

        Assert.False(ConditionEvaluator.Evaluate(config, Context())); // 14:30 is not < 09:00
    }

    [Fact]
    public void Evaluate_Date_Before()
    {
        var config = new ConditionNodeConfig(ConditionNodeConfig.MatchAll, [new ConditionRule(ConditionRule.FieldDate, ConditionEvaluator.OpBefore, "2026-12-31")]);

        Assert.True(ConditionEvaluator.Evaluate(config, Context()));
    }

    [Fact]
    public void Evaluate_MatchAll_RequiresEveryRule()
    {
        var config = new ConditionNodeConfig(ConditionNodeConfig.MatchAll,
        [
            new ConditionRule(ConditionRule.FieldTags, ConditionEvaluator.OpHas, "vip"),
            new ConditionRule("city", ConditionEvaluator.OpEquals, "Душанбе"),
        ]);

        Assert.False(ConditionEvaluator.Evaluate(config, Context(tags: ["vip"], variables: new() { ["city"] = "Хуҷанд" })));
        Assert.True(ConditionEvaluator.Evaluate(config, Context(tags: ["vip"], variables: new() { ["city"] = "Душанбе" })));
    }

    [Fact]
    public void Evaluate_MatchAny_RequiresOneRule()
    {
        var config = new ConditionNodeConfig(ConditionNodeConfig.MatchAny,
        [
            new ConditionRule(ConditionRule.FieldTags, ConditionEvaluator.OpHas, "vip"),
            new ConditionRule("city", ConditionEvaluator.OpEquals, "Душанбе"),
        ]);

        Assert.True(ConditionEvaluator.Evaluate(config, Context(tags: ["vip"], variables: new() { ["city"] = "Хуҷанд" })));
        Assert.False(ConditionEvaluator.Evaluate(config, Context(tags: [], variables: new() { ["city"] = "Хуҷанд" })));
    }
}
