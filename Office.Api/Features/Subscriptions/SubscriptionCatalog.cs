using Office.Api.Data.Entities;

namespace Office.Api.Features.Subscriptions;

public class SubscriptionPlanOptions
{
    public CustomerPlanTier Tier { get; set; }
    public decimal MonthlyPrice { get; set; }
}

public class PaymentCardOptions
{
    public string Bank { get; set; } = "";
    /// <summary>Stable key ("dc", "alif") the frontend maps to its own logo; unknown → no logo.</summary>
    public string BankCode { get; set; } = "";
    public string CardNumber { get; set; } = "";
    public string HolderName { get; set; } = "";
}

/// <summary>
/// Prices, durations and the cards customers pay to — read from "Subscriptions:*" config for
/// now. A tier missing from Plans simply isn't purchasable. Moves to the DB once the admin
/// page that edits it exists.
/// </summary>
public record SubscriptionCatalog(
    int TrialDays,
    string Currency,
    IReadOnlyList<int> DurationMonths,
    IReadOnlyList<SubscriptionPlanOptions> Plans,
    IReadOnlyList<PaymentCardOptions> PaymentCards)
{
    public static SubscriptionCatalog Load(IConfiguration configuration)
    {
        var section = configuration.GetSection("Subscriptions");

        return new SubscriptionCatalog(
            section.GetValue("TrialDays", 7),
            section.GetValue("Currency", "TJS")!,
            section.GetSection("DurationMonths").Get<int[]>() ?? [1],
            section.GetSection("Plans").Get<List<SubscriptionPlanOptions>>() ?? [],
            section.GetSection("PaymentCards").Get<List<PaymentCardOptions>>() ?? []);
    }

    public decimal? FindMonthlyPrice(CustomerPlanTier tier) =>
        Plans.FirstOrDefault(p => p.Tier == tier)?.MonthlyPrice;

    /// <summary>Matches on digits only — config may write "5058 2703 …", the client sends either form.</summary>
    public PaymentCardOptions? FindPaymentCard(string? cardNumber)
    {
        var digits = DigitsOnly(cardNumber);
        return digits.Length == 0 ? null : PaymentCards.FirstOrDefault(c => DigitsOnly(c.CardNumber) == digits);
    }

    private static string DigitsOnly(string? value) =>
        new((value ?? "").Where(char.IsAsciiDigit).ToArray());
}
