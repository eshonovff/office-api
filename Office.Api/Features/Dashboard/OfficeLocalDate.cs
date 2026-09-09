namespace Office.Api.Features.Dashboard;

/// <summary>
/// "Имрӯз" барои дашборд — вақти маҳаллӣ (Душанбе, UTC+5 доимӣ — Тоҷикистон DST надорад),
/// на UTC-и хом. Дар DB ҳама вақт UTC захира мешавад, вале "имрӯз"-и вазифа/тиреза танҳо дар
/// вақти маҳаллӣ маъно дорад: соати 19:00 UTC нимшаби маҳаллӣ аст, на 00:00 UTC. Pure.
/// </summary>
public static class OfficeLocalDate
{
    // Public/const — то дар дохили SQL-и EF LINQ бевосита истифода шавад (масалан
    // m.CreatedAt.AddHours(OfficeUtcOffsetHours) дар GroupBy-и heatmap), на танҳо дар C#-и
    // берун аз дархост. EF/Npgsql "+ interval '5 hours'"-ро дуруст тарҷума мекунад.
    public const int OfficeUtcOffsetHours = 5;

    private static readonly TimeSpan OfficeUtcOffset = TimeSpan.FromHours(OfficeUtcOffsetHours);

    public static DateOnly Today(DateTimeOffset utcNow) => DateOnly.FromDateTime(utcNow.UtcDateTime.Add(OfficeUtcOffset));

    /// <summary>
    /// Барои санҷиш (муқоиса бо натиҷаи SQL-и hourlyHeatmap, ки ҳамин ҳисобро дар СЮЛ мекунад,
    /// на бо ин метод) — на дар худи query-ҳо, чунки он ҷо бояд SQL-и тарҷумашаванда бошад
    /// (m.CreatedAt.AddHours(OfficeUtcOffsetHours).Hour), на даъвати ин методи C#.
    /// </summary>
    public static int LocalHour(DateTimeOffset utcNow) => utcNow.UtcDateTime.Add(OfficeUtcOffset).Hour;

    public static DayOfWeek LocalDayOfWeek(DateTimeOffset utcNow) => utcNow.UtcDateTime.Add(OfficeUtcOffset).DayOfWeek;
}
