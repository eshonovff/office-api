namespace Office.Api.Channels.Automation;

/// <summary>
/// Шакли typed-и AutomationRule.TriggerConfigJson. MatchMode: "keyword" (пешфарз — танҳо
/// комментарии дорои калима) ё "all" (ҳамаи комментарийҳо, бо огоҳии возеҳ дар UI).
/// PostScope: "all" ё "selected" (бо PostIds).
/// </summary>
/// <param name="PublicReplies">
/// Flows only (a comment trigger): short replies posted under the comment, taken in turn — the
/// staff "simple" rules keep theirs in AutomationActionConfig and ignore this. Null/empty: none.
/// </param>
public record AutomationTriggerConfig(string MatchMode, string[] Keywords, string PostScope, string[] PostIds, string[]? PublicReplies = null)
{
    public const int MaxPublicReplies = 5;
    public const int MaxPublicReplyLength = 300;

    public const string MatchModeKeyword = "keyword";
    public const string MatchModeAll = "all";
    public const string PostScopeAll = "all";
    public const string PostScopeSelected = "selected";
}

/// <summary>
/// Шакли typed-и AutomationRule.ConditionConfigJson. "{}" (пешфарзи Фазаи 10-и ҳамаи rule-ҳои
/// қаблӣ) → RequiresFollow=false худкор (default-и параметри record) — миграция лозим нашуд.
/// </summary>
public record AutomationConditionConfig(bool RequiresFollow = false);

/// <summary>
/// Як шохаи ҷавоб (OnMatch ё OnNotFollowing). Агар DmButtonUrl дода шавад, DmButtonTitle низ
/// ҳатмист — Instagram (Messenger Platform button template) бе сарлавҳа тугма қабул намекунад.
/// </summary>
public record AutomationReplyAction(string[] CommentReplies, string DmText, string? DmButtonUrl, string? DmButtonTitle = null);

/// <summary>
/// Шакли typed-и AutomationRule.ActionConfigJson (Фазаи 11 — ду шоха). OnNotFollowing null аст
/// агар condition_config.requiresFollow=false (шохаи дуюм ҳеҷ гоҳ истифода намешавад).
/// МУҲИМ: rule-ҳои Фазаи 10 (шакли ҳамвори кӯҳна) бо миграцияи AddFollowCheckCondition ба ин
/// шакл гулбанд карда шудаанд (OnMatch=кӯҳна, OnNotFollowing=null) — ниг. ҳуҷҷати фаза.
/// </summary>
public record AutomationActionConfig(AutomationReplyAction OnMatch, AutomationReplyAction? OnNotFollowing);
