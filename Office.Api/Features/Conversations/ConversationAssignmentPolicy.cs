namespace Office.Api.Features.Conversations;

/// <summary>Қарорҳои холиси таъиноти чат (claim/takeover/auto-release) — pure, бе DB, то тавон ҷудо тест кард.</summary>
public static class ConversationAssignmentPolicy
{
    /// <summary>
    /// Аввалин ҷавоб дар чати таъиннашуда онро ба фиристанда claim мекунад — таъиноти
    /// мавҷуда ҳеҷ гоҳ бо ин иваз намешавад, танҳо ҳолати null.
    /// </summary>
    public static bool ShouldClaimOnReply(Guid? currentAssignedTo) => currentAssignedTo is null;

    /// <summary>
    /// Хонда мешавад — гарчанде бо кӣ таъин шудааст: ҳама узви канал метавонанд бинанд.
    /// Фиристодан бошад маҳдуд аст — ин формула ҳамон ҷоест, ки маҳдудиятро татбиқ мекунад.
    /// Owner/Admin ҳамеша метавонанд бинависанд (ва такrop дар takeover/reassign); чати
    /// таъиннашуда ба ҳама кушода аст (аввалин ҷавоб ҳамон лаҳза claim мекунад); чати
    /// таъиншуда танҳо ба худи таъиншуда. Қасдан 24-соата lock нест — агар таъиншуда
    /// бемор шавад, мижоз набояд бе ҷавоб монад (takeover ҳал мекунад).
    /// </summary>
    public static bool CanSend(bool isOwnerOrAdmin, Guid? assignedTo, Guid userId) =>
        isOwnerOrAdmin || assignedTo is null || assignedTo == userId;
}
