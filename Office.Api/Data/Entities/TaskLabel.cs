namespace Office.Api.Data.Entities;

public class TaskLabel
{
    public Guid TaskId { get; set; }
    public TaskItem Task { get; set; } = null!;

    public Guid LabelId { get; set; }
    public Label Label { get; set; } = null!;
}
