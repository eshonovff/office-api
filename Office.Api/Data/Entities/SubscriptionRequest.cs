namespace Office.Api.Data.Entities;

public class SubscriptionRequest
{
    public Guid Id { get; set; }

    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public CustomerPlanTier Tier { get; set; }
    public int DurationMonths { get; set; }

    /// <summary>
    /// Base price plus random dirams (e.g. 200.37) — a card-to-card transfer carries no
    /// comment, so these cents are how a moderator matches the transfer in the bank statement.
    /// </summary>
    public decimal ExpectedAmount { get; set; }

    public SubscriptionRequestStatus Status { get; set; }

    public string? ReceiptPath { get; set; }
    public string? ReceiptFileName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }

    public Guid? ReviewedByUserId { get; set; }
    public string? ReviewedByUserName { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }
}
