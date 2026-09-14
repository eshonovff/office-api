using Office.Api.Channels.Automation;

namespace Office.Api.Features.CommentAutomation;

public record AutomationRuleListItem(
    Guid Id,
    string Name,
    bool IsActive,
    string TriggerType,
    AutomationTriggerConfig TriggerConfig,
    AutomationActionConfig ActionConfig,
    int CooldownMinutes,
    DateTimeOffset CreatedAt,
    int RunCount);

public record CreateAutomationRuleRequest(
    string Name,
    AutomationTriggerConfig TriggerConfig,
    AutomationActionConfig ActionConfig,
    int CooldownMinutes);

public record UpdateAutomationRuleRequest(
    string Name,
    AutomationTriggerConfig TriggerConfig,
    AutomationActionConfig ActionConfig,
    int CooldownMinutes);

public record SetAutomationRuleActiveRequest(bool IsActive);

/// <summary>
/// Dry-run: stateless — trigger_config-и ҲАНӮЗ ЗАХИРА НАШУДАи форма мегирад (на ruleId), то
/// қоидаи дар мобайни таҳрир низ санҷида шавад. Ҳеҷ чиз ба DB сабт/ба Meta фиристода намешавад.
/// </summary>
public record DryRunAutomationRuleRequest(AutomationTriggerConfig TriggerConfig, string CommentText, string? MediaId);

public record DryRunAutomationRuleResult(bool Matched, string? MatchedKeyword);

public record InstagramMediaListItem(string Id, string? MediaType, string? ImageUrl, string? Permalink, string? Caption, string? Timestamp);

public record InstagramMediaListResult(IReadOnlyList<InstagramMediaListItem> Items, string? NextCursor);
