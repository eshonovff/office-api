namespace Office.Api.Data.Entities;

/// <summary>
/// Шаблони омодаи flow (нодҳо+edge-ҳо ҳамчун JSON). Ҳангоми интихоб дар UI, нусха гирифта
/// мешавад ва ҳамчун Flow-и худи корбар сабт мегардад — ин ҷадвал худаш ҳеҷ гоҳ иҷро намешавад.
/// </summary>
public class FlowTemplate
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }

    /// <summary>{"nodes": [...], "edges": [...]} — шакли typed дар Channels/Flows/FlowTemplateDefinition.cs.</summary>
    public required string DefinitionJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
