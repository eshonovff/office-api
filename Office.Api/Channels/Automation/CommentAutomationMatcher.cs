namespace Office.Api.Channels.Automation;

/// <summary>
/// Мувофиқати як комментарий бо як AutomationTriggerConfig — pure, бе DB/HTTP. Ҳамин синф аз ду
/// ҷо истифода мешавад: коркарди воқеии webhook (CommentAutomationProcessor) ва endpoint-и
/// dry-run — то мантиқ ду ҷо нусхабардорӣ нашавад ва dry-run ҳамеша ҳамон натиҷаро диҳад, ки
/// production медиҳад.
/// </summary>
public static class CommentAutomationMatcher
{
    public readonly record struct MatchResult(bool Matched, string? MatchedKeyword);

    public static MatchResult Match(AutomationTriggerConfig config, string commentText, string? mediaId)
    {
        if (config.PostScope == AutomationTriggerConfig.PostScopeSelected &&
            (mediaId is null || !config.PostIds.Contains(mediaId)))
        {
            return new MatchResult(false, null);
        }

        if (config.MatchMode == AutomationTriggerConfig.MatchModeAll)
            return new MatchResult(true, null);

        // "keyword" — матни холӣ ё эмодзи-танҳо низ санҷида мешавад (contains-и оддӣ, ҳарфи
        // калон/хурд бетафовут): эмодзи ҳамчун матн муқоиса мешавад, calимаи холӣ ҳеҷ гоҳ мувофиқ намеояд.
        foreach (var keyword in config.Keywords)
        {
            if (keyword.Length > 0 && commentText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return new MatchResult(true, keyword);
        }

        return new MatchResult(false, null);
    }
}
