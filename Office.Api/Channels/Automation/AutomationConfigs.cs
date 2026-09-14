namespace Office.Api.Channels.Automation;

/// <summary>
/// Шакли typed-и AutomationRule.TriggerConfigJson. MatchMode: "keyword" (пешфарз — танҳо
/// комментарии дорои калима) ё "all" (ҳамаи комментарийҳо, бо огоҳии возеҳ дар UI).
/// PostScope: "all" ё "selected" (бо PostIds).
/// </summary>
public record AutomationTriggerConfig(string MatchMode, string[] Keywords, string PostScope, string[] PostIds)
{
    public const string MatchModeKeyword = "keyword";
    public const string MatchModeAll = "all";
    public const string PostScopeAll = "all";
    public const string PostScopeSelected = "selected";
}

/// <summary>Шакли typed-и AutomationRule.ActionConfigJson.</summary>
public record AutomationActionConfig(string[] CommentReplies, string DmText, string? DmButtonUrl);
