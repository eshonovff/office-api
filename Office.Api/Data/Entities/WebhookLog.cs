namespace Office.Api.Data.Entities;

public class WebhookLog
{
    public Guid Id { get; set; }
    public required string Provider { get; set; }
    public required string RawJson { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? Error { get; set; }
}
