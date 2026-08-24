namespace Office.Api.Data.Entities;

public class Conversation
{
    public Guid Id { get; set; }

    public Guid ChannelId { get; set; }
    public Channel Channel { get; set; } = null!;

    public required string ExternalId { get; set; }
    public string? ContactName { get; set; }
    public string? ContactAvatarUrl { get; set; }
    /// <summary>Instagram @handle (агар GetContactProfileAsync дошта бошад) — барои пайванди профил дар frontend. Facebook чунин майдон намедиҳад, null мемонад.</summary>
    public string? ContactUsername { get; set; }
    /// <summary>
    /// Вақте ки profile (ном/username/аватар) кӯшиш карда шуд — новобаста аз натиҷа. Ниг.
    /// InstagramContactProfileBackfillJob: бе ин майдон, чатҳое, ки Meta ҳеҷ гоҳ маълумот
    /// намедиҳад (масалан корбари ҳисобашро нест кардааст), ҳар иҷрои job аз нав кӯшиш мешаванд.
    /// </summary>
    public DateTimeOffset? ContactProfileFetchedAt { get; set; }

    public ConversationStatus Status { get; set; } = ConversationStatus.New;

    public Guid? AssignedTo { get; set; }
    public User? Assignee { get; set; }

    public DateTimeOffset? LastMessageAt { get; set; }
    public int UnreadCount { get; set; }
    public DateTimeOffset? WindowExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<Message> Messages { get; set; } = [];
}
