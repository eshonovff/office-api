namespace Office.Api.Features.Dashboard;

/// <summary>Қиматҳои иҷозатдодашудаи ?days= барои GET /api/dashboard/stats. Pure.</summary>
public static class StatsDaysRange
{
    public static readonly IReadOnlyList<int> Allowed = [7, 14, 30, 90];

    public static bool IsValid(int days) => Allowed.Contains(days);
}
