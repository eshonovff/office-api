namespace Office.Api.Channels.Messenger;

public enum MessengerSendMode
{
    /// <summary>Тиреза кушода — паёми оддӣ (messaging_type RESPONSE) кофист.</summary>
    Plain,

    /// <summary>Тиреза баста, вале то 7 рӯз аз паёми охирини воридотӣ — тег HUMAN_AGENT кор мекунад.</summary>
    Tag,

    /// <summary>7 рӯз ҳам гузаштааст — ҳеҷ роҳи қонунии фиристодан намондааст (на WhatsApp-монанд шаблон).</summary>
    Reject,
}

/// <summary>
/// Facebook/Instagram — бар хилофи WhatsApp — шаблон надоранд. Ба ҷои он "message tag":
/// берун аз тирезаи муқаррарӣ, теги HUMAN_AGENT паёмро то 7 рӯз аз паёми охирини воридотӣ
/// иҷозат медиҳад (на 24 соат). Ин ба Composer/operator ҳеҷ интихоб намедиҳад — ҳамеша
/// худкор татбиқ мешавад, чунки як оператори зинда ҷавоб медиҳад (табиист барои HUMAN_AGENT).
/// conversation.WindowExpiresAt аллакай = паёми охирини воридотӣ + 24с (ConversationWindowCalculator.
/// ComputeExpiresAt, новобаста аз provider) — пас марзи 7-рӯза бе майдони нави DB ҳисоб мешавад:
/// +6 рӯзи иловагӣ ба ҳамон арзиш.
/// </summary>
public static class MessengerSendModePlanner
{
    private static readonly TimeSpan TagExtension = TimeSpan.FromDays(6);

    public static MessengerSendMode Plan(DateTimeOffset? windowExpiresAt, DateTimeOffset now)
    {
        if (windowExpiresAt is null || windowExpiresAt > now)
            return MessengerSendMode.Plain;

        var tagDeadline = windowExpiresAt.Value + TagExtension;
        return now <= tagDeadline ? MessengerSendMode.Tag : MessengerSendMode.Reject;
    }
}
