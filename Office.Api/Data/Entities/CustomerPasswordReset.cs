namespace Office.Api.Data.Entities;

/// <summary>
/// One "forgot password" link sent to a мизоҷ. Only the SHA-256 of the token is stored — the
/// token itself exists in the email alone. Rows also count recent sends (cooldown, daily cap).
/// </summary>
public class CustomerPasswordReset
{
    public Guid Id { get; set; }

    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public required string TokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Set when the link is used — or when a newer link replaced it.</summary>
    public DateTimeOffset? ConsumedAt { get; set; }
}
