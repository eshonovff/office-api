namespace Office.Api.Features.Projects;

public record ProjectListItem(Guid Id, string Name, string Key, string? Color, bool IsArchived);

public record ProjectMemberDto(Guid UserId, string FullName, string Username);

public record BoardColumnDto(Guid Id, string Name, int OrderIndex, bool IsDoneColumn);

public record ProjectDetail(
    Guid Id,
    string Name,
    string Key,
    string? Color,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ProjectMemberDto> Members,
    IReadOnlyList<BoardColumnDto> Columns);

public record CreateProjectRequest(string Name, string Key, string? Color);

public record UpdateProjectRequest(string Name, string? Color);

public record SetProjectMembersRequest(IReadOnlyList<Guid> UserIds);

public record CreateColumnRequest(string Name, bool IsDoneColumn);

public record UpdateColumnRequest(string Name, bool IsDoneColumn);

public record ReorderColumnsRequest(IReadOnlyList<Guid> ColumnIds);

public record CreateLabelRequest(string Name, string? Color);

public record UpdateLabelRequest(string Name, string? Color);

public record LabelDto(Guid Id, string Name, string? Color);
