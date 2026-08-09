using Office.Api.Data.Entities;

namespace Office.Api.Channels;

/// <summary>Навсозии статуси расониши паёми аллакай фиристодашуда (sent/delivered/read/failed).</summary>
public record ParsedStatusUpdate(
    string MessageExternalId,
    MessageDeliveryStatus Status,
    DateTimeOffset UpdatedAt);
