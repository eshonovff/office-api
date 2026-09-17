using System.Text.Json;
using Office.Api.Data.Entities;

namespace Office.Api.Channels.Flows;

/// <summary>
/// Шакли typed-и FlowTemplate.DefinitionJson. Key (на Guid) — рамзи муваққатии дохили худи
/// шаблон, танҳо барои пайванди Edge→Node дар ҳамин JSON; ҳангоми нусхабардорӣ ба Flow-и воқеӣ
/// (POST .../flows/from-template), Key→Guid-и воқеӣ табдил меёбад.
/// </summary>
public record FlowTemplateNodeDefinition(string Key, string Type, JsonElement Config, double X, double Y);

public record FlowTemplateEdgeDefinition(string FromKey, string FromPort, string ToKey);

public record FlowTemplateDefinition(FlowTemplateNodeDefinition[] Nodes, FlowTemplateEdgeDefinition[] Edges);

/// <summary>
/// Key→Guid-и воқеӣ (боло) — як ҷо, то FlowTemplatesEndpoints ва тестҳо (FlowTemplateDeliveryTests)
/// айнан ҳамон мантиқро истифода баранд, на ду нусхаи мустақил, ки метавонанд аз ҳам дур шаванд.
/// </summary>
public static class FlowTemplateInstantiator
{
    public static (List<FlowNode> Nodes, List<FlowEdge> Edges) Instantiate(FlowTemplateDefinition definition, Guid flowId)
    {
        var idByKey = definition.Nodes.ToDictionary(n => n.Key, _ => Guid.CreateVersion7());

        var nodes = definition.Nodes.Select(node => new FlowNode
        {
            Id = idByKey[node.Key],
            FlowId = flowId,
            Type = Enum.Parse<FlowNodeType>(node.Type, ignoreCase: true),
            ConfigJson = node.Config.GetRawText(),
            X = node.X,
            Y = node.Y,
        }).ToList();

        var edges = definition.Edges.Select(edge => new FlowEdge
        {
            Id = Guid.CreateVersion7(),
            FlowId = flowId,
            FromNodeId = idByKey[edge.FromKey],
            FromPort = edge.FromPort,
            ToNodeId = idByKey[edge.ToKey],
        }).ToList();

        return (nodes, edges);
    }
}
