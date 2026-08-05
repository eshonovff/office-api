namespace Office.Api.Data.Entities;

public class RefreshToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public required string TokenHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? CreatedByIp { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
