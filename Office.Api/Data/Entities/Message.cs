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

    public string? MimeType { get; set; }
    public long? SizeBytes { get; set; }
    public string? OriginalFileName { get; set; }
    public string? MediaExternalId { get; set; }
    public int? VoiceDurationSeconds { get; set; }
    /// <summary>0-100 (на 0-1) — smallint[], то бе jsonb/floating-point барзиёд захира нашавад. DTO ба 0-1 табдил медиҳад.</summary>
    public short[]? WaveformPeaks { get; set; }
    public string? ThumbnailUrl { get; set; }
    public DateTimeOffset? MediaDeletedAt { get; set; }
    public string? MediaDownloadError { get; set; }

    public Guid? SentByUserId { get; set; }
    public User? SentByUser { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
