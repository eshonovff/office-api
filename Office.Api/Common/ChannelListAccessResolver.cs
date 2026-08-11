namespace Office.Api.Common;

public enum ChannelListScope
{
    All,
    MembersOnly,
    None,
}

/// <summary>
/// Кадом доираи канал барои GET /api/channels/mine — pure, бе DB. Ҳамон формулаи
/// ConversationAccessResolver.CanAccess бо isAssignedToUser=false (ҳамон тавре ки
/// InboxHub.JoinChannel HasAccessAsync-ро бо assignedTo:null даъват мекунад): корбари
/// only_assigned ба ягон гурӯҳи канали пурра дастрасӣ надорад — паёмҳои таъиншудаашро
/// тавассути гурӯҳи user:{id} мегирад, на channel:{id}.
/// </summary>
public static class ChannelListAccessResolver
{
    public static ChannelListScope Resolve(bool canSeeAllChannels, bool onlyAssigned) =>
        canSeeAllChannels ? ChannelListScope.All
        : onlyAssigned ? ChannelListScope.None
        : ChannelListScope.MembersOnly;

    /// <summary>
    /// Оё дар доираи ин scope (натиҷаи Resolve) як каналаи мушаххас дастрас аст — барои
    /// GET /channels/{id} ва /whatsapp-templates (санҷиши як канал, на филтри рӯйхат).
    /// </summary>
    public static bool CanAccessChannel(ChannelListScope scope, bool isChannelMember) =>
        scope switch
        {
            ChannelListScope.All => true,
            ChannelListScope.MembersOnly => isChannelMember,
            _ => false,
        };
}
