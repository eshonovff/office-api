using System.Net.Http.Headers;
using System.Text.Json;

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

    /// <summary>
    /// X-App-Usage: {"call_volume":N,...} — N (0-100) чӣ қадар ба маҳдудияти соатии app наздик
    /// аст. InstagramContactProfileBackfillJob ин рақамро истифода мебарад, то дар байни
    /// дархостҳо суст шавад пеш аз он ки воқеан ба rate limit расад.
    /// </summary>
    public static int? TryGetCallVolumePercent(HttpResponseHeaders headers)
    {
        if (!headers.TryGetValues("X-App-Usage", out var values))
            return null;

        var raw = values.FirstOrDefault();
        if (string.IsNullOrEmpty(raw))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.TryGetProperty("call_volume", out var el) && el.TryGetInt32(out var percent) ? percent : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
