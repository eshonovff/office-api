namespace Office.Api.Data.Entities;

public class RolePermission
{
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;

    public required string PermissionKey { get; set; }
}
