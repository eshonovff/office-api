using Office.Api.Common;

namespace Office.Api.Features.CustomerAnalytics;

/// <summary>
/// Whole days in Dushanbe time (UTC+5): from 00:00 of <see cref="From"/> to 24:00 of <see cref="To"/>.
/// At most <see cref="MaxDays"/> days, never in the future — a period is also a cost: the longer it
/// is, the more rows every query reads. Pure.
/// </summary>
public readonly record struct AnalyticsPeriod(DateOnly From, DateOnly To)
{
    public const int MaxDays = 366;
    public const int DefaultDays = 30;
    private static readonly DateOnly Earliest = new(2020, 1, 1);
    private static readonly TimeSpan Dushanbe = TimeSpan.FromHours(OfficeLocalDate.OfficeUtcOffsetHours);

    public int Days => To.DayNumber - From.DayNumber + 1;

    public DateTimeOffset StartUtc => new DateTimeOffset(From.ToDateTime(TimeOnly.MinValue), Dushanbe).ToUniversalTime();

    public DateTimeOffset EndUtc => new DateTimeOffset(To.AddDays(1).ToDateTime(TimeOnly.MinValue), Dushanbe).ToUniversalTime();

    /// <summary>The same number of days just before this period.</summary>
    public AnalyticsPeriod Previous => new(From.AddDays(-Days), From.AddDays(-1));

    public IEnumerable<DateOnly> Dates()
    {
        for (var date = From; date <= To; date = date.AddDays(1))
            yield return date;
    }

    /// <returns>The period, or why it can't be one. Nothing given — the last 30 days.</returns>
    public static (AnalyticsPeriod Period, string? Error) Parse(DateOnly? from, DateOnly? to, DateTimeOffset utcNow)
    {
        var today = OfficeLocalDate.Today(utcNow);
        var end = to ?? today;
        var start = from ?? end.AddDays(-(DefaultDays - 1));

        if (end > today)
            return (default, "Санаи охир дар оянда аст.");
        if (start > end)
            return (default, "Санаи аввал аз санаи охир дертар аст.");
        if (start < Earliest)
            return (default, "Сана хеле кӯҳна аст.");
        if (end.DayNumber - start.DayNumber + 1 > MaxDays)
            return (default, $"Давра то {MaxDays} рӯз бошад.");
        return (new AnalyticsPeriod(start, end), null);
    }
}
