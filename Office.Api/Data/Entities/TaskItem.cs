namespace Office.Api.Data.Entities;

public class TaskItem
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid ColumnId { get; set; }
    public BoardColumn Column { get; set; } = null!;

    public required string Title { get; set; }
    public string? Description { get; set; }

    public Guid? AssigneeId { get; set; }
    public User? Assignee { get; set; }

    public TaskPriority Priority { get; set; } = TaskPriority.Medium;
    public DateTimeOffset? DueDate { get; set; }
    public double Position { get; set; }

    public Guid CreatedBy { get; set; }
    public User Creator { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<TaskComment> Comments { get; set; } = [];
    public ICollection<TaskAttachment> Attachments { get; set; } = [];
    public ICollection<TaskLabel> TaskLabels { get; set; } = [];
    public ICollection<TaskActivity> Activities { get; set; } = [];
}
