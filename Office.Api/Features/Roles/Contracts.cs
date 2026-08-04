namespace Office.Api.Features.Roles;

public record RoleListItem(
    Guid Id,
    string Key,
    string Name,
    string? Description,
    bool IsSystem,
    IReadOnlyList<string> Permissions);

public record CreateRoleRequest(string Key, string Name, string? Description);

public record UpdateRoleRequest(string Name, string? Description);

public record SetRolePermissionsRequest(IReadOnlyList<string> PermissionKeys);
