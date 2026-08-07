namespace Office.Api.Data.Entities;

public class User
{
    public Guid Id { get; set; }
    public required string FullName { get; set; }
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public string? AvatarUrl { get; set; }
    public string? AvatarPath { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public DateOnly? BirthDate { get; set; }
    public string? Address { get; set; }
    public Gender? Gender { get; set; }
    public string? ContractDocumentPath { get; set; }
    public string? ContractDocumentFileName { get; set; }
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }
    public bool OnlyAssigned { get; set; }
    public int PermissionsVersion { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = [];
    public ICollection<UserPermission> UserPermissions { get; set; } = [];
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}
