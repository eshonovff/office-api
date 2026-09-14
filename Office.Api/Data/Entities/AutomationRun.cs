namespace Office.Api.Data.Entities;

public enum AutomationRunStatus
{
    Pending,
    Sent,
    Failed,

    /// <summary>Rule мувофиқ буд, вале actor+пост дар доираи cooldown буд — ҳеҷ чиз фиристода нашуд.</summary>
    SkippedCooldown,
}

/// <summary>
/// Логи иҷрои қоидаи автоматизатсия — як сатр барои ҳар комментарии коркардшуда (аз ҷумла
/// онҳое, ки cooldown партофт). ChannelId интихобан нест — тавассути Rule.ChannelId дастрас аст.
/// </summary>
public class AutomationRun
{
    public Guid Id { get; set; }
    public Guid RuleId { get; set; }
    public AutomationRule Rule { get; set; } = null!;

    public required string TriggerExternalId { get; set; }
    public required string ActorExternalId { get; set; }

    /// <summary>
    /// Media/post ID-и Instagram — дар спецификатсия дар рӯйхати сутунҳо набуд, вале
    /// "cooldown барои ҳамин actor дар ҳамин пост" бе он ғайриимкон аст (актор метавонад дар
    /// якчанд пости гуногун коментарий гузорад). Илова карда шуд бо ҳамин сабаб.
    /// </summary>
    public string? TargetMediaExternalId { get; set; }

    public string? MatchedKeyword { get; set; }
    public AutomationRunStatus CommentReplyStatus { get; set; }
    public AutomationRunStatus DmStatus { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
