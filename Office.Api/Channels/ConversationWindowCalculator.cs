using Office.Api.Data.Entities;

namespace Office.Api.Channels;

/// <summary>
/// Ҳисоби вақти нави тирезаи 24-соатаи посух (5.7) — pure, бе DB.
/// Танҳо паёми воридотӣ тирезаро дароз мекунад; паёми содиротӣ таъсир намерасонад.
/// </summary>
public static class ConversationWindowCalculator
{
    public static DateTimeOffset? ComputeExpiresAt(IReadOnlyCollection<ParsedWebhookMessage> messages, DateTimeOffset? currentExpiresAt)
    {
        var lastInboundAt = messages
            .Where(m => m.Direction == MessageDirection.Inbound)
            .Select(m => (DateTimeOffset?)m.SentAt)
            .Max();

        return lastInboundAt is not null ? lastInboundAt.Value.AddHours(24) : currentExpiresAt;
    }
}
