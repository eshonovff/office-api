using Office.Api.Data.Entities;

namespace Office.Api.Features.Subscriptions;

public class SubscriptionPlanOptions
{
    public CustomerPlanTier Tier { get; set; }
    public decimal MonthlyPrice { get; set; }
    public PlanLimitsOptions Limits { get; set; } = new();
}

/// <summary>
/// What a tier allows. A count left out of config (null) means unlimited — deliberately
/// "absent", not JSON null, which the configuration binder doesn't reliably map to null.
/// Shown on the pricing cards now; enforced once customers get their own channels and
/// automations (the multi-tenancy phase).
/// </summary>
public class PlanLimitsOptions
{
    public int? Accounts { get; set; }
    public int? ActiveAutomations { get; set; }
    public int? TeamMembers { get; set; }
    public bool WhatsAppBroadcasts { get; set; }
}

public class SubscriptionDurationOptions
{
    public int Months { get; set; }
    public decimal DiscountPercent { get; set; }
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
    TimeSpan PaymentWindow,
    string Currency,
    IReadOnlyList<SubscriptionDurationOptions> Durations,
    IReadOnlyList<SubscriptionPlanOptions> Plans,
    IReadOnlyList<PaymentCardOptions> PaymentCards)
{
    public static SubscriptionCatalog Load(IConfiguration configuration)
    {
        var section = configuration.GetSection("Subscriptions");

        return new SubscriptionCatalog(
            section.GetValue("TrialDays", 7),
            TimeSpan.FromMinutes(section.GetValue("PaymentWindowMinutes", 5)),
            section.GetValue("Currency", "TJS")!,
            section.GetSection("Durations").Get<List<SubscriptionDurationOptions>>()
                ?? [new SubscriptionDurationOptions { Months = 1 }],
            section.GetSection("Plans").Get<List<SubscriptionPlanOptions>>() ?? [],
            section.GetSection("PaymentCards").Get<List<PaymentCardOptions>>() ?? []);
    }

    public decimal? FindMonthlyPrice(CustomerPlanTier tier) =>
        Plans.FirstOrDefault(p => p.Tier == tier)?.MonthlyPrice;

    public SubscriptionDurationOptions? FindDuration(int months) =>
        Durations.FirstOrDefault(d => d.Months == months);

    /// <summary>Matches on digits only — config may write "5058 2703 …", the client sends either form.</summary>
    public PaymentCardOptions? FindPaymentCard(string? cardNumber)
    {
        var digits = DigitsOnly(cardNumber);
        return digits.Length == 0 ? null : PaymentCards.FirstOrDefault(c => DigitsOnly(c.CardNumber) == digits);
    }

    private static string DigitsOnly(string? value) =>
        new((value ?? "").Where(char.IsAsciiDigit).ToArray());
}
