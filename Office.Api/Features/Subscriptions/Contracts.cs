using Office.Api.Data.Entities;

namespace Office.Api.Features.Subscriptions;

public record SubscriptionPlanDto(string Tier, decimal MonthlyPrice);

public record PaymentCardDto(string Bank, string BankCode, string CardNumber, string HolderName);

public record SubscriptionCatalogResponse(
    string Currency,
    int TrialDays,
    IReadOnlyList<int> DurationMonths,
    IReadOnlyList<SubscriptionPlanDto> Plans,
    IReadOnlyList<PaymentCardDto> PaymentCards)
{
    public static SubscriptionCatalogResponse From(SubscriptionCatalog catalog) => new(
        catalog.Currency,
        catalog.TrialDays,
        catalog.DurationMonths,
        catalog.Plans.Select(p => new SubscriptionPlanDto(p.Tier.ToString(), p.MonthlyPrice)).ToList(),
        catalog.PaymentCards.Select(c => new PaymentCardDto(c.Bank, c.BankCode, c.CardNumber, c.HolderName)).ToList());
}

public record CreateSubscriptionRequest(string Tier, int Months);

public record RejectSubscriptionRequest(string Note);

/// <summary>What the customer sees about their own request.</summary>
public record SubscriptionRequestDto(
    Guid Id,
    string Tier,
    int DurationMonths,
    decimal ExpectedAmount,
    string Status,
    bool HasReceipt,
    string? PaidToBank,
    string? PaidToCardNumber,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaymentDeadline,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ReviewedAt,
    string? ReviewNote)
{
    /// <param name="paymentWindow">Only AwaitingPayment gets a deadline — the customer's countdown.</param>
    public static SubscriptionRequestDto From(SubscriptionRequest r, TimeSpan paymentWindow) => new(
        r.Id, r.Tier.ToString(), r.DurationMonths, r.ExpectedAmount, r.Status.ToString(), r.ReceiptPath is not null,
        r.PaidToBank, r.PaidToCardNumber, r.CreatedAt,
        r.Status == SubscriptionRequestStatus.AwaitingPayment
            ? PaymentWindow.DeadlineFor(r.CreatedAt, paymentWindow)
            : null,
        r.SubmittedAt, r.ReviewedAt, r.ReviewNote);
}

/// <summary>What a moderator sees — adds who the customer is and where the receipt is.</summary>
public record ModeratorSubscriptionRequestDto(
    Guid Id,
    Guid CustomerId,
    string CustomerEmail,
    string CustomerFullName,
    string Tier,
    int DurationMonths,
    decimal ExpectedAmount,
    string Status,
    string? ReceiptUrl,
    string? ReceiptFileName,
    string? PaidToBank,
    string? PaidToCardNumber,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt,
    string? ReviewedByUserName,
    DateTimeOffset? ReviewedAt,
    string? ReviewNote)
{
    public static ModeratorSubscriptionRequestDto From(SubscriptionRequest r) => new(
        r.Id, r.CustomerId, r.Customer.Email, r.Customer.FullName, r.Tier.ToString(), r.DurationMonths,
        r.ExpectedAmount, r.Status.ToString(),
        r.ReceiptPath is not null ? $"/api/subscription-requests/{r.Id}/receipt" : null,
        r.ReceiptFileName, r.PaidToBank, r.PaidToCardNumber,
        r.CreatedAt, r.SubmittedAt, r.ReviewedByUserName, r.ReviewedAt, r.ReviewNote);
}
