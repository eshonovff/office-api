using Office.Api.Data.Entities;

namespace Office.Api.Features.Conversations;

/// <summary>Кадом паём ҳангоми "хонда шуд" бояд `Read` шавад (6.10) — pure, бе DB.</summary>
public static class UnreadMessageSelector
{
    public static bool ShouldMarkAsRead(MessageDirection direction, MessageDeliveryStatus currentStatus) =>
        direction == MessageDirection.Inbound && currentStatus != MessageDeliveryStatus.Read;
}
