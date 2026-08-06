namespace Office.Api.Data.Entities;

public class Message
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;

    public MessageDirection Direction { get; set; }
    public MessageType Type { get; set; }
    public string? Body { get; set; }
    public string? MediaUrl { get; set; }
    public string? ExternalId { get; set; }
    public MessageDeliveryStatus DeliveryStatus { get; set; } = MessageDeliveryStatus.Pending;
    public bool IsInternalNote { get; set; }

    public Guid? SentByUserId { get; set; }
    public User? SentByUser { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
