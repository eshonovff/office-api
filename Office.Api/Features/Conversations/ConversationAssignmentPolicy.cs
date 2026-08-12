namespace Office.Api.Features.Conversations;

/// <summary>Қарорҳои холиси таъиноти чат (claim/takeover/auto-release) — pure, бе DB, то тавон ҷудо тест кард.</summary>
public static class ConversationAssignmentPolicy
{
    /// <summary>
    /// Аввалин ҷавоб дар чати таъиннашуда онро ба фиристанда claim мекунад — таъиноти
    /// мавҷуда ҳеҷ гоҳ бо ин иваз намешавад, танҳо ҳолати null.
    /// </summary>
    public static bool ShouldClaimOnReply(Guid? currentAssignedTo) => currentAssignedTo is null;
}
