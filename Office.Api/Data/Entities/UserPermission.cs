namespace Office.Api.Data.Entities;

public class UserPermission
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public required string PermissionKey { get; set; }
    public bool IsGranted { get; set; }
}
