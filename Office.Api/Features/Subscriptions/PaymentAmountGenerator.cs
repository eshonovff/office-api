namespace Office.Api.Features.Subscriptions;

public static class PaymentAmountGenerator
{
    private const int MinDirams = 1;
    private const int MaxDirams = 99;

    /// <summary>
    /// <paramref name="baseAmount"/> plus 0.01–0.99, preferring an amount no other open request
    /// is currently waiting on. If all 99 are taken the amount may repeat — the receipt image is
    /// still reviewed by a human, the cents are only a matching aid.
    /// </summary>
    public static decimal Generate(decimal baseAmount, IReadOnlySet<decimal> takenAmounts, Random random)
    {
        var free = new List<int>();
        for (var dirams = MinDirams; dirams <= MaxDirams; dirams++)
        {
            if (!takenAmounts.Contains(baseAmount + dirams / 100m))
                free.Add(dirams);
        }

        var chosen = free.Count > 0
            ? free[random.Next(free.Count)]
            : random.Next(MinDirams, MaxDirams + 1);

        return baseAmount + chosen / 100m;
    }
}
