namespace Office.Api.Data.Entities;

public enum FlowSessionStatus
{
    Active,
    Waiting,
    Finished,
    Failed,
}

/// <summary>
/// Сабаби "waiting" — бе ин ResumeFromMessageAsync намедонад паёми нави корбар ба кадом маъно
/// аст (илова аз спека, зарур барои иҷрои дуруст, ниг. ҳуҷҷати фазаи 12).
/// </summary>
public enum FlowWaitReason
{
    Delay,
    ButtonClick,
    CollectInput,

    /// <summary>Тирезаи 24-соата баста буд — ҳамон нод дубора кӯшиш мекунад вақте паёми нав тирезаро мекушояд.</summary>
    WindowClosed,
}

/// <summary>
/// Ҳолати ҳар корбар (contact = Conversation, ниг. FlowConfigs.cs барои сабаб) дар дохили як
/// flow. Як contact метавонад дар як лаҳза танҳо як сессияи фаъол/интизор дар ҳар flow дошта
/// бошад — FlowTriggerProcessor пеш аз сохтани сессияи нав инро тафтиш мекунад.
/// </summary>
public class FlowSession
{
    public Guid Id { get; set; }
    public Guid FlowId { get; set; }
    public Flow Flow { get; set; } = null!;

    public Guid ContactId { get; set; }
    public Conversation Contact { get; set; } = null!;

    /// <summary>
    /// comment_id/message_id-и рӯйдоде, ки ин сессияро сар кард — идемпотентӣ (ҳамон алгуи
    /// AutomationRun.TriggerExternalId-и Фазаи 10/11): агар Meta webhook-ро такрор фиристад,
    /// FlowTriggerProcessor сессияи дуюм намесозад.
    /// </summary>
    public string? TriggerExternalId { get; set; }

    public Guid? CurrentNodeId { get; set; }
    public string VariablesJson { get; set; } = "{}";
    public FlowSessionStatus Status { get; set; } = FlowSessionStatus.Active;
    public FlowWaitReason? WaitReason { get; set; }
    public DateTimeOffset? ResumeAt { get; set; }

    /// <summary>Hangfire job-и барномарезишуда (delay) — то flow таҳрир/қатъ шавад, бекор карда шавад.</summary>
    public string? ScheduledJobId { get; set; }

    /// <summary>Ҳимояи ҳалқа — аз спека: "ҳадди аксар 50 қадам дар як сессия, баъд failed".</summary>
    public int StepCount { get; set; }

    /// <summary>Сабаби Failed (масалан "ҳалқа: аз 50 қадам гузашт", хатои http_request) — барои дебаг.</summary>
    public string? Error { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Логи ҳар қадам — барои омори кумулятивӣ ("шумораи контактҳое, ки ба нод расиданд").
/// current_node_id-и FlowSession танҳо ҳозираро медиҳад, на таърихро — ниг. ҳуҷҷати фазаи 12
/// барои баррасии хароҷот (ин ҷадвал ба ҷои сутуни шумориши рақобатӣ дар FlowNode интихоб шуд).
/// </summary>
public class FlowSessionStep
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public FlowSession Session { get; set; } = null!;
    public Guid NodeId { get; set; }
    public string? FromPort { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
