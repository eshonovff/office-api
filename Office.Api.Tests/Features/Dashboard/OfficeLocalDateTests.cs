using Office.Api.Features.Dashboard;

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
}
