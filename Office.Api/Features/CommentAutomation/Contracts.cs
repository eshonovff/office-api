using Office.Api.Channels.Automation;

namespace Office.Api.Features.CommentAutomation;

public record AutomationRuleListItem(
    Guid Id,
    string Name,
    bool IsActive,
    string TriggerType,
    AutomationTriggerConfig TriggerConfig,
    AutomationConditionConfig ConditionConfig,
    AutomationActionConfig ActionConfig,
    int CooldownMinutes,
    DateTimeOffset CreatedAt,
    int RunCount);

public record CreateAutomationRuleRequest(
    string Name,
    AutomationTriggerConfig TriggerConfig,
    AutomationConditionConfig ConditionConfig,
    AutomationActionConfig ActionConfig,
    int CooldownMinutes);

public record UpdateAutomationRuleRequest(
    string Name,
    AutomationTriggerConfig TriggerConfig,
    AutomationConditionConfig ConditionConfig,
    AutomationActionConfig ActionConfig,
    int CooldownMinutes);

public record SetAutomationRuleActiveRequest(bool IsActive);

/// <summary>
/// Dry-run: stateless — trigger_config-и ҲАНӮЗ ЗАХИРА НАШУДАи форма мегирад (на ruleId), то
/// қоидаи дар мобайни таҳрир низ санҷида шавад. Ҳеҷ чиз ба DB сабт намешавад. Агар
/// ConditionConfig.RequiresFollow ва ActorExternalId дода шуда бошанд, follow-check ВОҚЕАН
/// иҷро мешавад (хонданӣ, кэшдор — бехатар); ба Meta ҳеҷ паём фиристода намешавад.
/// </summary>
public record DryRunAutomationRuleRequest(
    AutomationTriggerConfig TriggerConfig, string CommentText, string? MediaId,
    AutomationConditionConfig? ConditionConfig = null, string? ActorExternalId = null);

public record DryRunAutomationRuleResult(bool Matched, string? MatchedKeyword, string? FollowCheckResult);

public record InstagramMediaListItem(string Id, string? MediaType, string? ImageUrl, string? Permalink, string? Caption, string? Timestamp);

public record InstagramMediaListResult(IReadOnlyList<InstagramMediaListItem> Items, string? NextCursor);
