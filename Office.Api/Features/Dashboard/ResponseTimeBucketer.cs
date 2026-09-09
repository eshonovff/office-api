namespace Office.Api.Features.Dashboard;

/// <summary>
/// Сабадҳои гистограммаи вақти ҷавоби аввал. Pure. Дарозии манфӣ қасдан истисно мепартояд —
/// query-и SQL (ниг. DashboardStatsQueryService) сохторан аз "аввалин содиротӣ БАЪДИ аввалин
/// воридотӣ" месозад, пас манфӣ ҳеҷ гоҳ набояд ба ин ҷо расад; агар расид, хатои воқеӣ аст,
/// на ҳолати муқаррарӣ, ки бояд хомӯшона пинҳон шавад.
/// </summary>
public static class ResponseTimeBucketer
{
    public const string Under5Min = "<5min";
    public const string From5To15Min = "5-15min";
    public const string From15To60Min = "15-60min";
    public const string From1To4Hours = "1-4h";
    public const string Over4Hours = ">4h";

    public static readonly IReadOnlyList<string> AllBuckets =
        [Under5Min, From5To15Min, From15To60Min, From1To4Hours, Over4Hours];

    public static string Bucket(TimeSpan responseTime)
    {
        if (responseTime < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(responseTime), responseTime, "Вақти ҷавоб наметавонад манфӣ бошад.");

        if (responseTime < TimeSpan.FromMinutes(5))
            return Under5Min;
        if (responseTime < TimeSpan.FromMinutes(15))
            return From5To15Min;
        if (responseTime < TimeSpan.FromHours(1))
            return From15To60Min;
        if (responseTime < TimeSpan.FromHours(4))
            return From1To4Hours;

        return Over4Hours;
    }
}
