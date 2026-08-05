namespace Office.Api.Data.Entities;

public class TaskComment
{
    public Guid Id { get; set; }

    public Guid TaskId { get; set; }
    public TaskItem Task { get; set; } = null!;

    public Guid AuthorId { get; set; }
    public User Author { get; set; } = null!;

    public required string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
