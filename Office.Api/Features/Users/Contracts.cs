namespace Office.Api.Features.Users;

public record RoleSummary(Guid Id, string Key, string Name);

public record UserPermissionExceptionDto(string PermissionKey, bool IsGranted);

public record UserListItem(
    Guid Id,
    string FullName,
    string Username,
    bool IsActive,
    IReadOnlyList<RoleSummary> Roles);

public record UserDetail(
    Guid Id,
    string FullName,
    string Username,
    string? Phone,
    string? AvatarUrl,
    bool IsActive,
    bool MustChangePassword,
    bool OnlyAssigned,
    IReadOnlyList<RoleSummary> Roles,
    IReadOnlyList<UserPermissionExceptionDto> PermissionExceptions);

public record CreateUserRequest(string FullName, string Username, string? Phone);

public record CreateUserResponse(Guid Id, string Username, string TemporaryPassword);

public record UpdateUserRequest(string FullName, string? Phone, bool OnlyAssigned);

public record SetUserRolesRequest(IReadOnlyList<Guid> RoleIds);

public record SetUserPermissionsRequest(IReadOnlyList<UserPermissionExceptionDto> Exceptions);

public record ResetPasswordResponse(string TemporaryPassword);
