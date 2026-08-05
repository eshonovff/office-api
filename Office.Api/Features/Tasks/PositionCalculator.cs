namespace Office.Api.Features.Tasks;

public static class PositionCalculator
{
    public const double Step = 1000;
    public const double MinGap = 0.0001;

    /// <summary>
    /// beforePosition/afterPosition — position-ҳои ҳамсояҳои мустақими нуқтаи гузоштан.
    /// null аз тарафи чап = аввали колонка; null аз тарафи рост = охири колонка.
    /// </summary>
    public static double Calculate(double? beforePosition, double? afterPosition)
    {
        if (beforePosition is null && afterPosition is null)
            return Step;

        if (beforePosition is null)
            return afterPosition!.Value / 2;

        if (afterPosition is null)
            return beforePosition.Value + Step;

        return (beforePosition.Value + afterPosition.Value) / 2;
    }

    public static bool NeedsReindex(double? beforePosition, double? afterPosition, double computed)
    {
        if (beforePosition is not null && Math.Abs(computed - beforePosition.Value) < MinGap)
            return true;

        if (afterPosition is not null && Math.Abs(afterPosition.Value - computed) < MinGap)
            return true;

        return false;
    }
}
