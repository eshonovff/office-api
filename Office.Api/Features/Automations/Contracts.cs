namespace Office.Api.Features.Automations;

/// <summary>
/// Рӯйхати ЯГОНА барои ду сарчашма (ниг. ҳуҷҷати Фазаи 12 — тасмими #2): automation_rules
/// (намоиш ҳамчун Type="simple") ва flows (Type="flow"). Union дар сатҳи ин endpoint аст, на
/// дар DB — automation_rules физикӣ кӯчонида НАШУДААСТ.
/// </summary>
public record AutomationListItem(
    Guid Id,
    string Type,
    string Name,
    bool IsActive,
    Guid ChannelId,
    string ChannelName,
    int ContactCount,
    /// <summary>
    /// Танҳо барои Type="flow" ҳисоб карда мешавад (finished/total сессияҳо). Барои "simple"
    /// null аст — қоидаи V1/V2 мафҳуми "сессия" надорад, ки конверсия аз он ҳисоб шавад
    /// (ниг. "Он чи иҷро нашуд" дар ҳуҷҷат).
    /// </summary>
    double? ConversionPercent,
    DateTimeOffset CreatedAt);
