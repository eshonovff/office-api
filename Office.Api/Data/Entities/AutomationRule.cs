namespace Office.Api.Data.Entities;

/// <summary>
/// Қоидаи автоматизатсия (Фазаи 10, V1: танҳо коментарии Instagram). Сохтори
/// trigger/condition/action қасдан jsonb аст ва аз ҳам ҷудо нигоҳ дошта мешавад — вақте
/// конструктори визуалӣ меояд, танҳо UI иваз мешавад, на модел. condition_config холӣ
/// мемонад дар V1 (ҷои тасдиқи обуна дар оянда).
/// </summary>
public class AutomationRule
{
    public Guid Id { get; set; }
    public Guid ChannelId { get; set; }
    public Channel Channel { get; set; } = null!;
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Ҳозир танҳо "instagram_comment" — майдони string, на enum, то навъҳои нав бе миграция илова шаванд.</summary>
    public required string TriggerType { get; set; }

    /// <summary>{matchMode, keywords[], postScope, postIds[]} — ниг. AutomationConfigs.AutomationTriggerConfig.</summary>
    public required string TriggerConfigJson { get; set; }

    /// <summary>Холӣ ("{}"") дар V1 — ҷои тасдиқи обуна дар оянда.</summary>
    public string ConditionConfigJson { get; set; } = "{}";

    /// <summary>{commentReplies[], dmText, dmButtonUrl} — ниг. AutomationConfigs.AutomationActionConfig.</summary>
    public required string ActionConfigJson { get; set; }

    public int CooldownMinutes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<AutomationRun> Runs { get; set; } = [];
}
