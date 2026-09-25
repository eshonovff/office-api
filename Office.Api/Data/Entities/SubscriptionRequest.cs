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

    /// <summary>
    /// The company card the customer says they paid to — tells the moderator which bank's
    /// history to look in. Snapshotted (not a key into config) so the record stays readable
    /// after a card is replaced or removed from Subscriptions:PaymentCards.
    /// </summary>
    public string? PaidToBank { get; set; }
    public string? PaidToCardNumber { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }

    public Guid? ReviewedByUserId { get; set; }
    public string? ReviewedByUserName { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }
}
