using Office.Api.Channels.Automation;

namespace Office.Api.Channels.Flows;

/// <summary>
/// Flow.TriggerType — string, на enum (навъҳои нав бе миграция). Фазаи 20: ҷавоб ва қайд дар
/// сторис (ниг. docs/phases/phase-20-growth-tools.md).
/// </summary>
public static class FlowTriggerTypes
{
    public const string Comment = "instagram_comment";
    public const string Dm = "instagram_dm";
    public const string StoryReply = "instagram_story_reply";
    public const string StoryMention = "instagram_story_mention";

    public static readonly IReadOnlyList<string> All = [Comment, Dm, StoryReply, StoryMention];

    /// <summary>
    /// DM ва қайд пост/сторис надоранд — «интихобшуда» ҳеҷ гоҳ мувофиқ намеомад (дом дар UI).
    /// </summary>
    public static bool AllowsSelectedScope(string triggerType) => triggerType is Comment or StoryReply;

    /// <summary>Қайд дар сторис матн надорад — калима маъно надорад.</summary>
    public static bool AllowsKeywords(string triggerType) => triggerType is not StoryMention;

    /// <summary>Null — дуруст; вагарна сабаб (барои validator).</summary>
    public static string? CheckConfig(string triggerType, AutomationTriggerConfig config)
    {
        if (!AllowsSelectedScope(triggerType) && config.PostScope != AutomationTriggerConfig.PostScopeAll)
            return "Барои ин триггер танҳо postScope='all'.";
        if (!AllowsKeywords(triggerType) && config.MatchMode != AutomationTriggerConfig.MatchModeAll)
            return "Қайд дар сторис матн надорад — танҳо matchMode='all'.";
        return null;
    }
}
