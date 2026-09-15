namespace Office.Api.Data.Entities;

/// <summary>
/// Тегҳои contact (= Conversation, ниг. FlowConfigs.cs). Калиди аслӣ (ContactId, Tag) —
/// ActionNodeExecutor (Kind=add_tags/remove_tags) ва ConditionNodeExecutor (Field=tags)
/// истифода мебаранд.
/// </summary>
public class ContactTag
{
    public Guid ContactId { get; set; }
    public Conversation Contact { get; set; } = null!;
    public required string Tag { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
