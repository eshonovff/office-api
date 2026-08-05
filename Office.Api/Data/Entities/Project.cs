namespace Office.Api.Data.Entities;

public class Project
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Key { get; set; }
    public string? Color { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<ProjectMember> Members { get; set; } = [];
    public ICollection<BoardColumn> Columns { get; set; } = [];
    public ICollection<TaskItem> Tasks { get; set; } = [];
    public ICollection<Label> Labels { get; set; } = [];
}
