namespace Office.Api.Data.Entities;

public class Label
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public required string Name { get; set; }
    public string? Color { get; set; }

    public ICollection<TaskLabel> TaskLabels { get; set; } = [];
}
