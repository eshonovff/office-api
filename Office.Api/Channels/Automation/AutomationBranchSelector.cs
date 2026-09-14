using Office.Api.Data.Entities;

namespace Office.Api.Channels.Automation;

/// <summary>
/// Интихоби шохаи ҷавоб бар асоси натиҷаи follow-check — pure, бе DB/HTTP. "Unknown" ва
/// "requiresFollow=false" (followCheckResult=null) ҳарду ба OnMatch мераванд — муштарӣ ҳеҷ гоҳ
/// бе ҷавоб намемонад агар тафтиш ноком шавад ё умуман лозим набошад.
/// </summary>
public static class AutomationBranchSelector
{
    public static AutomationReplyAction Select(AutomationActionConfig actionConfig, FollowCheckResult? followCheckResult) =>
        followCheckResult == FollowCheckResult.NotFollowing && actionConfig.OnNotFollowing is not null
            ? actionConfig.OnNotFollowing
            : actionConfig.OnMatch;
}
