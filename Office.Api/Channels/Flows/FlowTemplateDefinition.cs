using System.Text.Json;

namespace Office.Api.Channels.Flows;

/// <summary>
/// Шакли typed-и FlowTemplate.DefinitionJson. Key (на Guid) — рамзи муваққатии дохили худи
/// шаблон, танҳо барои пайванди Edge→Node дар ҳамин JSON; ҳангоми нусхабардорӣ ба Flow-и воқеӣ
/// (POST .../flows/from-template), Key→Guid-и воқеӣ табдил меёбад.
/// </summary>
public record FlowTemplateNodeDefinition(string Key, string Type, JsonElement Config, double X, double Y);

public record FlowTemplateEdgeDefinition(string FromKey, string FromPort, string ToKey);

public record FlowTemplateDefinition(FlowTemplateNodeDefinition[] Nodes, FlowTemplateEdgeDefinition[] Edges);
