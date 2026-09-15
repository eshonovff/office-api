namespace Office.Api.Data.Entities;

/// <summary>
/// Автоматизатсияи навъи "flow" (Фазаи 12) — граф аз нодҳо, конструктори визуалӣ. Паҳлӯи
/// AutomationRule-и Фазаи 10 (навъи "simple"), на ба ҷои он — GET /api/automations ду
/// сарчашмаро дар сатҳи endpoint муттаҳид мекунад (ниг. ҳуҷҷати фазаи 12: бе кӯчонидани
/// физикии automation_rules, то ҳеҷ қоидаи ҷорӣ вайрон нашавад).
/// </summary>
public class Flow
{
    public Guid Id { get; set; }
    public Guid ChannelId { get; set; }
    public Channel Channel { get; set; } = null!;
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// "instagram_comment" | "instagram_dm" — спека мегӯяд "webhook (коментарий ё DM) → ёфтани
    /// flow-и мувофиқ", вале мантиқи мувофиқатро намедиҳад. Ҳамон шакли AutomationRule-и Фазаи
    /// 10 такрор истифода мешавад (TriggerType+TriggerConfigJson), то ҳарду система якхела кор
    /// кунанд ва FlowTriggerProcessor CommentAutomationMatcher-и мавҷударо айнан истифода барад.
    /// </summary>
    public required string TriggerType { get; set; }
    public required string TriggerConfigJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<FlowNode> Nodes { get; set; } = [];
    public ICollection<FlowEdge> Edges { get; set; } = [];
}

public enum FlowNodeType
{
    Message,
    Condition,
    Action,
    Note,
}

/// <summary>
/// Як нод дар граф. ConfigJson шакли typed дорад вобаста ба Type — ниг.
/// Channels/Flows/FlowConfigs.cs (MessageNodeConfig/ConditionNodeConfig/ActionNodeConfig/NoteNodeConfig).
/// "Ноди аввал" (FlowNode бе ягон FlowEdge.ToNodeId ба он ишора мекунад) ҳамеша аввалин
/// иҷрошаванда аст, вақте Flow.TriggerType/TriggerConfigJson мувофиқат кунад.
/// </summary>
public class FlowNode
{
    public Guid Id { get; set; }
    public Guid FlowId { get; set; }
    public Flow Flow { get; set; } = null!;
    public FlowNodeType Type { get; set; }
    public required string ConfigJson { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
}

/// <summary>
/// Пайванди байни ду нод. FromPort ҷудокунандаи шоха аст: "default" (баромади оддӣ),
/// "button:{index}" (аз тугмаи мушаххас), "match"/"nomatch" (натиҷаи шарт).
/// </summary>
public class FlowEdge
{
    public Guid Id { get; set; }
    public Guid FlowId { get; set; }
    public Flow Flow { get; set; } = null!;
    public Guid FromNodeId { get; set; }
    public required string FromPort { get; set; }
    public Guid ToNodeId { get; set; }
}
