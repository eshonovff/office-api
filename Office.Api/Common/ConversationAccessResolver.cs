namespace Office.Api.Common;

/// <summary>
/// Формулаи доступ ба conversation (6.12-6.14) — pure, бе DB, то тавон ҷудо тест кард.
/// </summary>
public static class ConversationAccessResolver
{
    public static bool CanAccess(bool canSeeAllChannels, bool isChannelMember, bool onlyAssigned, bool isAssignedToUser) =>
        (canSeeAllChannels || isChannelMember) && (!onlyAssigned || isAssignedToUser);
}
