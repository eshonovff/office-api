namespace Office.Api.Data.Entities;

public class BoardColumn
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public required string Name { get; set; }
    public int OrderIndex { get; set; }
    public bool IsDoneColumn { get; set; }

    public ICollection<TaskItem> Tasks { get; set; } = [];
}
