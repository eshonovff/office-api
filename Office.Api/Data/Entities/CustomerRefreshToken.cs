namespace Office.Api.Data.Entities;

/// <summary>Ҳамон алгуи RefreshToken (кормандон), вале барои Customer — ҷадвали ҷудогона.</summary>
public class CustomerRefreshToken
{
    public Guid Id { get; set; }

    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public required string TokenHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? CreatedByIp { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
