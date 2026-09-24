namespace Office.Api.Data.Entities;

public class Channel
{
    public Guid Id { get; set; }
    public ChannelType Type { get; set; }
    public required string Name { get; set; }
    public required string ExternalId { get; set; }
    public string? CredentialsEncrypted { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>
    /// Соҳиби канал (Фазаи 14): null = канали ширкат (кормандон), вагарна мизоҷи худсабтшуда.
    /// Ҳама чизи ба канал тобеъ (flow, conversation, message, …) соҳибро аз ин ҷо мерос мегирад —
    /// ҷудокунӣ бо global query filter-и AppDbContext, на бо Where-и дастӣ.
    /// </summary>
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    /// <summary>
    /// Instagram's app-scoped user id (/me.id at connect), distinct from ExternalId (/me.user_id,
    /// the business account id webhooks use). Meta's data-deletion callback may identify the user
    /// by it, so it's what DataDeletionJob matches on (with ExternalId as a fallback).
    /// Null for channels connected before this was recorded, and for non-Instagram channels.
    /// </summary>
    public string? MetaAppScopedUserId { get; set; }

    /// <summary>
    /// Вақти анҷоми эътибори токен (Instagram long-lived: ~60 рӯз) — танҳо вақте провайдер
    /// онро медиҳад (ig_exchange_token/refresh). InstagramTokenRefreshJob каналҳои ба ин наздикро
    /// худкор нав мекунад; WhatsApp/Facebook system-user токен маъмулан мӯҳлат надорад, null мемонад.
    /// </summary>
    public DateTimeOffset? CredentialsExpiresAt { get; set; }

    /// <summary>
    /// Meta бо хатои "токен эътибор надорад" (ё нав кардани худкор ноком шуд) ҷавоб дод — то
    /// пайвастшавии дастӣ/OAuth дубора, паёмҳо фиристода намешаванд. Дар UI намоён (ниг. report
    /// барои сабаб: пеш аз ин чунин нокомӣ хомӯшона буд, танҳо notification-и Owner).
    /// </summary>
    public bool RequiresReconnect { get; set; }

    /// <summary>
    /// Facebook/Instagram: обунаи webhook (Page ва/ё App сатҳ) пас аз /connect тасдиқ нашуд —
    /// сабаби мушаххас (барои UI, на танҳо булии хомӯш). Ниг. report 2026-08-25: сабаби воқеии
    /// "Facebook паём намерасад" маҳз ҳамин буд (App Dashboard-и Meta нопурра буд), вале то ин
    /// пайдо шуданаш хомӯшона гум мешуд. null = обуна тасдиқ шуд ё ин канал (WhatsApp) чунин
    /// обунае надорад.
    /// </summary>
    public string? WebhookSetupWarning { get; set; }

    public ICollection<ChannelMember> Members { get; set; } = [];
    public ICollection<Conversation> Conversations { get; set; } = [];
}
