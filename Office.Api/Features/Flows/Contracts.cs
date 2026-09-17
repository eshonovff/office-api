using System.Text.Json;
using Office.Api.Channels.Automation;

namespace Office.Api.Features.Flows;

public record FlowListItem(Guid Id, Guid ChannelId, string Name, bool IsActive, string TriggerType, int NodeCount, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>
/// Config-и ҳар нод шакли typed надорад дар ин сатҳ — вобаста ба Type фарқ мекунад (ниг.
/// Channels/Flows/FlowConfigs.cs). JsonElement бе тағйир интиқол дода мешавад: canvas
/// (frontend) месозад/мехонад, backend танҳо нигоҳ медорад (ба ғайр аз санҷиши FluentValidation).
/// </summary>
public record FlowNodeDto(Guid Id, string Type, JsonElement Config, double X, double Y);

public record FlowEdgeDto(Guid Id, Guid FromNodeId, string FromPort, Guid ToNodeId);

public record FlowDetail(
    Guid Id, Guid ChannelId, string Name, bool IsActive, string TriggerType, AutomationTriggerConfig TriggerConfig,
    IReadOnlyList<FlowNodeDto> Nodes, IReadOnlyList<FlowEdgeDto> Edges, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public record CreateFlowRequest(string Name, string TriggerType, AutomationTriggerConfig TriggerConfig);

public record UpdateFlowRequest(string Name, string TriggerType, AutomationTriggerConfig TriggerConfig);

public record SetFlowActiveRequest(bool IsActive);

public record FlowNodeInput(Guid Id, string Type, JsonElement Config, double X, double Y);

public record FlowEdgeInput(Guid Id, Guid FromNodeId, string FromPort, Guid ToNodeId);

/// <summary>
/// Autosave: canvas ҳамаи граф (nodes+edges)-ро якҷоя мефиристад — backend ба ҷои муқоисаи
/// тағйирот (diff), ҳамаро иваз мекунад (табиатан идемпотентӣ, оддӣ барои debounce-и такрорӣ).
/// </summary>
public record UpdateFlowGraphRequest(IReadOnlyList<FlowNodeInput> Nodes, IReadOnlyList<FlowEdgeInput> Edges);

public record FlowNodeStat(Guid NodeId, int ContactCount);

public record FlowStats(int TotalSessions, int FinishedSessions, int ActiveOrWaitingSessions, int FailedSessions, IReadOnlyList<FlowNodeStat> Nodes);

public record FlowTemplateListItem(Guid Id, string Name, string? Description);

/// <summary>MediaId — attachment_id-и дубора-истифодашавандаи Meta (ниг. InstagramProvider.UploadMediaAsync).</summary>
public record UploadFlowMediaResult(string MediaId, string BlockType);
