namespace Office.Api.Common;

public enum ChannelListScope
{
    All,
    MembersOnly,
    AssignedOnly,
}

/// <summary>
/// Joinable = оё корбар бояд channel:{id}-и SignalR-ро бипайвандад (InboxHub.JoinChannel).
/// only_assigned корбар паёмҳои таъиншудаашро тавассути гурӯҳи user:{id} мегирад, на
/// channel:{id} (ҳамон алгуи ConversationAccessResolver.CanAccess бо isAssignedToUser=false,
/// ки InboxHub.JoinChannel HasAccessAsync-ро бо assignedTo:null даъват мекунад) — бинобар
/// ин барои AssignedOnly ҳамеша false аст, ҳатто агар канал дар рӯйхат бошад.
/// </summary>
public readonly record struct ChannelListPolicy(ChannelListScope Scope, bool Joinable);

/// <summary>Кадом доираи канал барои GET /api/channels/mine — pure, бе DB.</summary>
public static class ChannelListAccessResolver
{
    public static ChannelListPolicy Resolve(bool canSeeAllChannels, bool onlyAssigned) =>
        canSeeAllChannels ? new ChannelListPolicy(ChannelListScope.All, Joinable: true)
        : onlyAssigned ? new ChannelListPolicy(ChannelListScope.AssignedOnly, Joinable: false)
        : new ChannelListPolicy(ChannelListScope.MembersOnly, Joinable: true);

    /// <summary>
    /// Оё дар доираи ин scope як каналаи мушаххас дастрас аст — барои GET /channels/{id}
    /// ва /whatsapp-templates (санҷиши як канал, на филтри рӯйхат). isInScope — далели DB-и
    /// мувофиқ ба scope (узвияти канал барои MembersOnly, сӯҳбати таъиншуда барои AssignedOnly);
    /// барои All нодида гирифта мешавад.
    /// </summary>
    public static bool CanAccessChannel(ChannelListScope scope, bool isInScope) =>
        scope == ChannelListScope.All || isInScope;
}
