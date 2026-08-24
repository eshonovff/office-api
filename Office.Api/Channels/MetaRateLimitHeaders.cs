using System.Net.Http.Headers;

namespace Office.Api.Channels;

/// <summary>
/// Танҳо барои ташхис: агар Meta воқеан rate limit гузошта бошад, ин сарлавҳаҳо (агар мавҷуд
/// бошанд) онро мегӯянд — то бигӯем оё хатои "Service temporarily unavailable" (is_transient:true)
/// воқеан rate limit аст ё оддӣ нокомии муваққатии Meta. Ҳеҷ токен/маълумоти махфӣ надоранд —
/// бехатар барои лог.
/// </summary>
public static class MetaRateLimitHeaders
{
    private static readonly string[] HeaderNames =
    [
        "X-App-Usage",
        "X-Business-Use-Case-Usage",
        "X-Ad-Account-Usage",
        "X-Page-Usage",
        "Retry-After",
    ];

    public static string? Describe(HttpResponseHeaders headers)
    {
        List<string>? parts = null;
        foreach (var name in HeaderNames)
        {
            if (!headers.TryGetValues(name, out var values))
                continue;

            parts ??= [];
            parts.Add($"{name}={string.Join(",", values)}");
        }

        return parts is null ? null : string.Join("; ", parts);
    }
}
