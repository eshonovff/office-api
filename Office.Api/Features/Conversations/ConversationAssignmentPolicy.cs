using Office.Api.Data.Entities;

namespace Office.Api.Features.Conversations;

/// <summary>Қарорҳои холиси таъиноти чат (claim/takeover/auto-release) — pure, бе DB, то тавон ҷудо тест кард.</summary>
public static class ConversationAssignmentPolicy
{
    /// <summary>
    /// Аввалин ҷавоб дар чати таъиннашуда онро ба фиристанда claim мекунад — таъиноти
    /// мавҷуда ҳеҷ гоҳ бо ин иваз намешавад, танҳо ҳолати null. Ёддошти дохилӣ (item 6)
    /// claim намекунад — он ба мижоз намерасад, пас "аввалин ҷавоб" нест, танҳо қайд аст.
    /// </summary>
    public static bool ShouldClaimOnReply(Guid? currentAssignedTo, bool isInternalNote) =>
        currentAssignedTo is null && !isInternalNote;

    /// <summary>
    /// Хонда мешавад — гарчанде бо кӣ таъин шудааст: ҳама узви канал метавонанд бинанд.
    /// Фиристодан бошад маҳдуд аст — ин формула ҳамон ҷоест, ки маҳдудиятро татбиқ мекунад.
    /// Owner/Admin ҳамеша метавонанд бинависанд (ва такрор дар takeover/reassign); чати
    /// таъиннашуда ба ҳама кушода аст (аввалин ҷавоб ҳамон лаҳза claim мекунад); чати
    /// таъиншуда танҳо ба худи таъиншуда. Қасдан 24-соата lock нест — агар таъиншуда
    /// бемор шавад, мижоз набояд бе ҷавоб монад (takeover ҳал мекунад).
    /// </summary>
    public static bool CanSend(bool isOwnerOrAdmin, Guid? assignedTo, Guid userId) =>
        isOwnerOrAdmin || assignedTo is null || assignedTo == userId;

    /// <summary>
    /// Чати пӯшида ҳеҷ гоҳ auto-release намешавад — чати пӯшида "фаъол" нест, дасткорӣ
    /// намехоҳад. Нуқтаи сар — охирин фаъолияти худи таъиншуда дар ин чат (паёми
    /// содиротии ӯ), на таъиноти умумии чат; агар ӯ ҳанӯз ягон бор нафиристода бошад,
    /// лаҳзаи худи таъинот (assignedAt) нуқтаи сар мешавад.
    /// </summary>
    public static bool ShouldAutoRelease(
        ConversationStatus status,
        DateTimeOffset assignedAt,
        DateTimeOffset? lastActivityByAssignee,
        DateTimeOffset now,
        TimeSpan inactivityThreshold)
    {
        if (status == ConversationStatus.Closed)
            return false;

        var baseline = lastActivityByAssignee ?? assignedAt;
        return now - baseline >= inactivityThreshold;
    }
}
