namespace Office.Api.Features.Users;

public record RoleSummary(Guid Id, string Key, string Name);

public record UserPermissionExceptionDto(string PermissionKey, bool IsGranted);

public record UserListItem(
    Guid Id,
    string FullName,
    string Username,
    string? Phone,
    string? Email,
    DateOnly? BirthDate,
    int? Age,
    string? Address,
    string? Gender,
    string? AvatarUrl,
    bool HasContractDocument,
    bool IsActive,
    bool MustChangePassword,
    IReadOnlyList<RoleSummary> Roles);

public record UserDetail(
    Guid Id,
    string FullName,
    string Username,
    string? Phone,
    string? Email,
    DateOnly? BirthDate,
    int? Age,
    string? Address,
    string? Gender,
    bool HasContractDocument,
    string? AvatarUrl,
    bool IsActive,
    bool MustChangePassword,
    bool OnlyAssigned,
    IReadOnlyList<RoleSummary> Roles,
    IReadOnlyList<UserPermissionExceptionDto> PermissionExceptions);

public record CreateUserRequest(
    string FullName,
    string Phone,
    string? Email,
    DateOnly? BirthDate,
    string? Address,
    string? Gender);

public record CreateUserResponse(Guid Id, string Username, string TemporaryPassword, bool SmsSent, string? AvatarUrl);

public record UpdateUserRequest(
    string FullName,
    string? Email,
    DateOnly? BirthDate,
    string? Address,
    string? Gender,
    bool OnlyAssigned);

public record SetUserRolesRequest(IReadOnlyList<Guid> RoleIds);

public record SetUserPermissionsRequest(IReadOnlyList<UserPermissionExceptionDto> Exceptions);

public record ResetPasswordResponse(string TemporaryPassword, bool SmsSent);
