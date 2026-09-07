namespace Office.Api.Features.Dashboard;

/// <summary>
/// Сабаби "channelIssues"-и дашборд (GET /api/dashboard) — якчанд шарт метавонанд ҳамзамон
/// рост бошанд (масалан ҳам requires_reconnect, ҳам токен наздик ба анҷом), пас матни якхела
/// бояд ҳамаашро дар бар гирад, на танҳо якеро. Pure, бе DB.
/// </summary>
public static class ChannelIssueReasonResolver
{
    /// <summary>
    /// null = ягон мушкил нест (канал бояд аз WHERE-и SQL берун монда бошад ин ҷо). is_active
    /// қасдан ин ҷо НЕСТ (2026-08-26, А2) — канали қасдан ғайрифаъолшуда мушкил нест, панҷ
    /// канали тестии хомӯшкардашуда ҳамчун "channelIssues" мушкилоти воқеиро мепӯшониданд.
    /// </summary>
    public static string? Resolve(
        bool requiresReconnect,
        string? webhookSetupWarning,
        DateTimeOffset? credentialsExpiresAt,
        DateTimeOffset now,
        TimeSpan expiringSoonThreshold)
    {
        List<string>? reasons = null;

        if (requiresReconnect)
            (reasons ??= []).Add("Пайвастшавӣ лозим аст.");

        if (!string.IsNullOrEmpty(webhookSetupWarning))
            (reasons ??= []).Add(webhookSetupWarning);

        if (credentialsExpiresAt is not null && credentialsExpiresAt <= now + expiringSoonThreshold)
            (reasons ??= []).Add($"Токен то {credentialsExpiresAt.Value:dd.MM} анҷом меёбад.");

        return reasons is null ? null : string.Join(" ", reasons);
    }
}
