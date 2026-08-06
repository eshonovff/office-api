namespace Office.Api.Channels;

/// <summary>
/// Аз лоти паёмҳои гирифташуда, паёмҳои воқеан навро ҷудо мекунад —
/// ҳам нисбат ба паёмҳои аллакай дар база буда, ҳам такрори дохили худи лот.
/// Ин формулаи idempotency-и вазифаи 4.10 аст (бе такя ба база, тестшаванда).
/// </summary>
public static class MessageIdempotencyPlanner
{
    public static IReadOnlyList<ParsedWebhookMessage> FilterNew(
        IReadOnlyCollection<ParsedWebhookMessage> incoming,
        IReadOnlySet<string> existingExternalIds)
    {
        var seen = new HashSet<string>(existingExternalIds, StringComparer.Ordinal);
        var result = new List<ParsedWebhookMessage>();

        foreach (var message in incoming)
        {
            if (seen.Add(message.MessageExternalId))
                result.Add(message);
        }

        return result;
    }
}
