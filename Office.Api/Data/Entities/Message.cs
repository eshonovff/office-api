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

    /// <summary>
    /// Пайванди воқеии Instagram барои Reel/Post/Story-и мубодилашуда (масалан
    /// instagram.com/reel/&lt;code&gt;/) — ҳеҷ гоҳ зеркашӣ намешавад: тасдиқшуда (2026-08-24,
    /// curl зидди production) ин ҳамеша САҲИФАИ ВЕБ аст, на URL-и CDN-и медиа. Танҳо ҳамчун
    /// пайванди берунӣ нигоҳ дошта мешавад — на дар матн, то frontend ба таври мустақим ва
    /// боэътимод "Кушодан дар Instagram" созад.
    /// </summary>
    public string? ExternalContentUrl { get; set; }
    /// <summary>"Reel" | "Post" | "Story" — танҳо вақте ExternalContentUrl пур аст.</summary>
    public string? ExternalContentKind { get; set; }

    public Guid? SentByUserId { get; set; }
    public User? SentByUser { get; set; }
    /// <summary>
    /// Snapshot of the sender's display name at send time. SentByUserId is ON DELETE SET
    /// NULL, so without this a deleted user's replies would lose their attribution — this
    /// column is the source of truth for display, independent of whether the user still
    /// exists or SentByUser was included in the query.
    /// </summary>
    public string? SentByUserName { get; set; }

    // Template sends need this at dispatch time, but dispatch now happens in a delayed
    // background job — long after the original HTTP request (and its SendMessageRequest)
    // is gone. Persisted here instead of re-derived, same reasoning as SentByUserName.
    public string? TemplateName { get; set; }
    public string? TemplateLanguage { get; set; }
    public string? TemplateParametersJson { get; set; }

    /// <summary>Пур мешавад ҳар вақте DeliveryStatus ба Failed мегузарад — "чаро" на танҳо "нашуд".</summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// Ҷавоби хоми Meta Graph API (JSON), фақат вақте FailureReason аз GraphApiException омадааст —
    /// барои debug дар frontend (details/tooltip-и пӯшида), FailureReason-и худ ҳамеша матни
    /// инсонфаҳм мемонад (ниг. MetaErrorTranslator).
    /// </summary>
    public string? FailureDetail { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
