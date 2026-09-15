namespace Office.Api.Channels.Flows;

/// <summary>
/// Ҳама маълумоти лозим барои санҷидани як ConditionNodeConfig, аллакай гирифташуда (аз DB/Graph
/// API) — то худи баҳодиҳӣ pure бошад ва бе он тестпазир. FlowEngine ин контекстро месозад
/// (масалан IsFollowing-ро танҳо агар rule-и "subscription" мавҷуд бошад мепурсад).
/// </summary>
public record ConditionContext(
    IReadOnlyDictionary<string, string> Variables,
    IReadOnlySet<string> Tags,
    bool? IsFollowing,
    DateTimeOffset Now);

/// <summary>
/// Спека операторҳои дақиқро намедиҳад ("op" — умумӣ). Маҷмӯи ҳадди ақали оқилона интихоб шуд:
/// equals/not_equals/contains барои сатр (variable, weekday), has/not_has барои tags,
/// before/after барои time/date. Field-и ғайри-калидвожа (на "subscription"/"tags"/"time"/
/// "date"/"weekday") ҳамчун номи тағйирёбанда тафсир мешавад — то майдони алоҳидаи "кадом
/// тағйирёбанда" лозим набошад.
/// </summary>
public static class ConditionEvaluator
{
    public const string OpEquals = "equals";
    public const string OpNotEquals = "not_equals";
    public const string OpContains = "contains";
    public const string OpHas = "has";
    public const string OpNotHas = "not_has";
    public const string OpBefore = "before";
    public const string OpAfter = "after";

    public static bool Evaluate(ConditionNodeConfig config, ConditionContext context)
    {
        if (config.Rules.Length == 0)
            return true;

        return config.Match == ConditionNodeConfig.MatchAny
            ? config.Rules.Any(r => EvaluateRule(r, context))
            : config.Rules.All(r => EvaluateRule(r, context));
    }

    private static bool EvaluateRule(ConditionRule rule, ConditionContext context) => rule.Field switch
    {
        ConditionRule.FieldSubscription => context.IsFollowing == true,
        ConditionRule.FieldTags => EvaluateTags(rule, context.Tags),
        ConditionRule.FieldTime => EvaluateTime(rule, context.Now),
        ConditionRule.FieldDate => EvaluateDate(rule, context.Now),
        ConditionRule.FieldWeekday => EvaluateString(context.Now.DayOfWeek.ToString(), rule.Op, rule.Value),
        _ => EvaluateString(context.Variables.GetValueOrDefault(rule.Field), rule.Op, rule.Value),
    };

    private static bool EvaluateTags(ConditionRule rule, IReadOnlySet<string> tags) =>
        rule.Op == OpNotHas ? !tags.Contains(rule.Value) : tags.Contains(rule.Value);

    private static bool EvaluateString(string? actual, string op, string expected) => op switch
    {
        OpNotEquals => !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
        OpContains => actual is not null && actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
        _ => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
    };

    private static bool EvaluateTime(ConditionRule rule, DateTimeOffset now)
    {
        if (!TimeSpan.TryParse(rule.Value, out var boundary))
            return false;

        return rule.Op == OpAfter ? now.TimeOfDay >= boundary : now.TimeOfDay < boundary;
    }

    private static bool EvaluateDate(ConditionRule rule, DateTimeOffset now)
    {
        if (!DateOnly.TryParse(rule.Value, out var boundary))
            return false;

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        return rule.Op switch
        {
            OpAfter => today > boundary,
            OpBefore => today < boundary,
            _ => today == boundary,
        };
    }
}
