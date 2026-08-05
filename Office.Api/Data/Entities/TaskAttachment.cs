namespace Office.Api.Data.Entities;

public class TaskAttachment
{
    public Guid Id { get; set; }

    public Guid TaskId { get; set; }
    public TaskItem Task { get; set; } = null!;

    public required string FileName { get; set; }
    public required string FilePath { get; set; }
    public long SizeBytes { get; set; }

    public Guid UploadedBy { get; set; }
    public User Uploader { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
}
