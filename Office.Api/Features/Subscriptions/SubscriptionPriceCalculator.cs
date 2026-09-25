namespace Office.Api.Features.Subscriptions;

public static class SubscriptionPriceCalculator
{
    /// <summary>
    /// monthlyPrice × months, minus the duration's discount, rounded DOWN to whole somoni.
    /// Whole because PaymentAmountGenerator adds the random dirams on top (200 → 200.37);
    /// down so the customer never pays more than the advertised discount promises.
    /// </summary>
    public static decimal Calculate(decimal monthlyPrice, int months, decimal discountPercent)
    {
        var full = monthlyPrice * months;
        return Math.Floor(full * (100 - discountPercent) / 100);
    }
}
