namespace Office.Api.Data.Entities;

public enum MessageDeliveryStatus
{
    Pending,
    Sent,
    Delivered,
    Read,
    Failed,
    /// <summary>
    /// Бекор шуд дар давоми тирезаи ирсоли таъхирӣ (item 5) — ҳеҷ гоҳ ба провайдер
    /// нарасидааст. Ба query-и WebhookProcessor.ProcessStatusUpdatesAsync таъсир намекунад:
    /// он танҳо паёмҳои дорои ExternalId-ро мебинад, ва паёми Cancelled ҳеҷ гоҳ ExternalId
    /// намегирад (провайдер ҳеҷ гоҳ даъват намешавад).
    /// </summary>
    Cancelled,
}
