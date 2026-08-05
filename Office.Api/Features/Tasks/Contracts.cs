using Office.Api.Features.Projects;

namespace Office.Api.Features.Tasks;

public record TaskListItem(
    Guid Id,
    Guid ProjectId,
    Guid ColumnId,
    string Title,
    Guid? AssigneeId,
    string? AssigneeName,
    string Priority,
    DateTimeOffset? DueDate,
    double Position,
    IReadOnlyList<LabelDto> Labels);

public record TaskDetail(
    Guid Id,
    Guid ProjectId,
    Guid ColumnId,
    string Title,
    string? Description,
    Guid? AssigneeId,
    string? AssigneeName,
    string Priority,
    DateTimeOffset? DueDate,
    double Position,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<LabelDto> Labels);

public record BoardColumnWithTasks(Guid Id, string Name, int OrderIndex, bool IsDoneColumn, IReadOnlyList<TaskListItem> Tasks);

public record BoardResponse(IReadOnlyList<BoardColumnWithTasks> Columns);

public record CreateTaskRequest(
    Guid ProjectId,
    Guid ColumnId,
    string Title,
    string? Description,
    Guid? AssigneeId,
    string Priority,
    DateTimeOffset? DueDate,
    IReadOnlyList<Guid>? LabelIds);

public record UpdateTaskRequest(
    string Title,
    string? Description,
    string Priority,
    DateTimeOffset? DueDate,
    IReadOnlyList<Guid>? LabelIds);

public record MoveTaskRequest(Guid ColumnId, Guid? BeforeTaskId, Guid? AfterTaskId);

public record AssignTaskRequest(Guid? AssigneeId);

public record TaskCommentDto(Guid Id, Guid AuthorId, string AuthorName, string Body, DateTimeOffset CreatedAt);

public record CreateCommentRequest(string Body);

public record TaskAttachmentDto(Guid Id, string FileName, long SizeBytes, Guid UploadedBy, DateTimeOffset CreatedAt);

public record TaskActivityDto(Guid Id, Guid UserId, string UserName, string Action, string? PayloadJson, DateTimeOffset CreatedAt);
