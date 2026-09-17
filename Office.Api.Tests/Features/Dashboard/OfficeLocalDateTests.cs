using Office.Api.Common;

namespace Office.Api.Tests.Features.Dashboard;

public class OfficeLocalDateTests
{
    [Fact]
    public void Today_JustBeforeLocalMidnight_StillPreviousLocalDay()
    {
        // 18:59:59 UTC = 23:59:59 маҳаллӣ (UTC+5) — ҳанӯз рӯзи ҳамон рӯз.
        var utcNow = new DateTimeOffset(2026, 3, 10, 18, 59, 59, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2026, 3, 10), OfficeLocalDate.Today(utcNow));
    }

    [Fact]
    public void Today_ExactlyAtLocalMidnight_IsTheNextLocalDay()
    {
        // 19:00:00 UTC = 00:00:00 маҳаллӣ (UTC+5)-и рӯзи БАЪДӢ — марз худаш дигар мешавад.
        var utcNow = new DateTimeOffset(2026, 3, 10, 19, 0, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2026, 3, 11), OfficeLocalDate.Today(utcNow));
    }

    [Fact]
    public void Today_MiddleOfLocalDay_MatchesLocalCalendarDate()
    {
        // 08:00 UTC = 13:00 маҳаллӣ, ҳамон рӯз.
        var utcNow = new DateTimeOffset(2026, 3, 10, 8, 0, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2026, 3, 10), OfficeLocalDate.Today(utcNow));
    }

    [Fact]
    public void Today_CrossesMonthBoundaryCorrectly()
    {
        // 19:30 UTC 2026-02-28 = 00:30 маҳаллӣ 2026-03-01.
        var utcNow = new DateTimeOffset(2026, 2, 28, 19, 30, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2026, 3, 1), OfficeLocalDate.Today(utcNow));
    }

    /// <summary>
    /// Регрессия: FlowEngine.ExecuteConditionNodeAsync то ин ислоҳ DateTimeOffset.UtcNow-и хомро
    /// мустақим ба ConditionContext медод — шарти "аз соати 9 то 18" воқеан аз 14:00 то 23:00 UTC
    /// санҷида мешуд (5 соат хато, бе ягон хатои возеҳ). Now(...) бояд .TimeOfDay/.DayOfWeek/
    /// .UtcDateTime-и АЛЛАКАЙ ба вақти Душанбе гузаронидашударо диҳад, то ConditionEvaluator
    /// (ки ин се хосиятро мустақим мехонад) дуруст кор кунад бе тағйир додани худи он.
    /// </summary>
    [Fact]
    public void Now_ShiftsTimeOfDayAndDayOfWeekToOfficeLocal_NotUtc()
    {
        // 20:30 UTC, панҷшанбе — маҳаллӣ 01:30, ҷумъа (+5 соат аз нимашаб мегузарад).
        var utcNow = new DateTimeOffset(2026, 3, 12, 20, 30, 0, TimeSpan.Zero); // 2026-03-12 = Панҷшанбе

        var local = OfficeLocalDate.Now(utcNow);

        Assert.Equal(new TimeSpan(1, 30, 0), local.TimeOfDay);
        Assert.Equal(DayOfWeek.Friday, local.DayOfWeek);
        Assert.Equal(new DateOnly(2026, 3, 13), DateOnly.FromDateTime(local.UtcDateTime));
    }
}
