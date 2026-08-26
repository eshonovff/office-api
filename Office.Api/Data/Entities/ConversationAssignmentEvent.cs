namespace Office.Api.Data.Entities;

public enum ConversationAssignmentReason
{
    /// <summary>Аввалин ҷавоб дар чати таъиннашуда (6.2).</summary>
    ClaimedOnReply,
    /// <summary>Оператори дигар бо тугмаи "гирифтан" аз таъиншудаи қаблӣ гирифт (6.3).</summary>
    Takeover,
    /// <summary>PATCH /conversations/{id} — таъини дастӣ (Owner/Admin/Manager).</summary>
    Reassigned,
    /// <summary>Job-и Hangfire — таъиншуда муддате чизе нафиристод (6.4).</summary>
    AutoReleased,
}

/// <summary>
/// Таърихи таъиноти чат — кӣ, кай, аз кӣ ба кӣ ва чаро. Номҳо snapshot мешаванд (на танҳо
/// FK), ҳамон сабабе, ки Message.SentByUserName-ро водор кард: нест кардани корбар набояд
/// таърихро вайрон кунад. Ин ҷадвал "барои дидан дар оянда" аст — ҳоло ягон GET endpoint
/// надорад, танҳо сабт мешавад.
/// </summary>
public class ConversationAssignmentEvent
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;

    public Guid? FromUserId { get; set; }
    public string? FromUserName { get; set; }

    public Guid? ToUserId { get; set; }
    public string? ToUserName { get; set; }

    public ConversationAssignmentReason Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
