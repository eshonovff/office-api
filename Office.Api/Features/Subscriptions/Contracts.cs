using Office.Api.Data.Entities;

namespace Office.Api.Features.Subscriptions;

/// <summary>What one duration of a plan costs — computed here so the page never re-derives it.</summary>
public record SubscriptionPriceDto(int Months, decimal DiscountPercent, decimal FullPrice, decimal Total);

/// <summary>Null count = unlimited.</summary>
public record PlanLimitsDto(int? Accounts, int? ActiveAutomations, int? TeamMembers, bool WhatsAppBroadcasts);

public record SubscriptionPlanDto(
    string Tier, decimal MonthlyPrice, PlanLimitsDto Limits, IReadOnlyList<SubscriptionPriceDto> Prices);

public record PaymentCardDto(string Bank, string BankCode, string CardNumber, string HolderName);

public record SubscriptionCatalogResponse(
    string Currency,
    int TrialDays,
    IReadOnlyList<SubscriptionPlanDto> Plans,
    IReadOnlyList<PaymentCardDto> PaymentCards)
{
    public static SubscriptionCatalogResponse From(SubscriptionCatalog catalog) => new(
        catalog.Currency,
        catalog.TrialDays,
        catalog.Plans.Select(p => PlanDto(p, catalog.Durations)).ToList(),
        catalog.PaymentCards.Select(c => new PaymentCardDto(c.Bank, c.BankCode, c.CardNumber, c.HolderName)).ToList());

    private static SubscriptionPlanDto PlanDto(
        SubscriptionPlanOptions plan, IReadOnlyList<SubscriptionDurationOptions> durations) => new(
        plan.Tier.ToString(),
        plan.MonthlyPrice,
        new PlanLimitsDto(
            plan.Limits.Accounts, plan.Limits.ActiveAutomations, plan.Limits.TeamMembers, plan.Limits.WhatsAppBroadcasts),
        durations.Select(d => new SubscriptionPriceDto(
            d.Months,
            d.DiscountPercent,
            plan.MonthlyPrice * d.Months,
            SubscriptionPriceCalculator.Calculate(plan.MonthlyPrice, d.Months, d.DiscountPercent))).ToList());
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

public record PendingCountResponse(int Count);

/// <summary>What a moderator sees — adds who the customer is and where the receipt is.</summary>
public record ModeratorSubscriptionRequestDto(
    Guid Id,
    Guid CustomerId,
    string CustomerEmail,
    string CustomerFullName,
    string Tier,
    int DurationMonths,
    decimal ExpectedAmount,
    string Currency,
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
    public static ModeratorSubscriptionRequestDto From(SubscriptionRequest r, string currency) => new(
        r.Id, r.CustomerId, r.Customer.Email, r.Customer.FullName, r.Tier.ToString(), r.DurationMonths,
        r.ExpectedAmount, currency, r.Status.ToString(),
        r.ReceiptPath is not null ? $"/api/subscription-requests/{r.Id}/receipt" : null,
        r.ReceiptFileName, r.PaidToBank, r.PaidToCardNumber,
        r.CreatedAt, r.SubmittedAt, r.ReviewedByUserName, r.ReviewedAt, r.ReviewNote);
}
