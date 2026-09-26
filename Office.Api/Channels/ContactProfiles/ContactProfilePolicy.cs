using Office.Api.Data.Entities;

namespace Office.Api.Channels.ContactProfiles;

/// <summary>
/// When to ask Meta again who a contact is. Instagram and Facebook give a name and picture only
/// through their profile API — and Instagram says nothing about a person until they have written
/// to the account, so a chat the account started (or an auto-reply to a comment) begins without
/// them. A message from the person is when asking again can work. Pure.
/// </summary>
public static class ContactProfilePolicy
{
    /// <summary>Who they are is unknown — asked again on their next message, at most this often.</summary>
    public static readonly TimeSpan RetryUnknown = TimeSpan.FromMinutes(1);

    /// <summary>Known, but no picture yet (many people have none) — asked again at most this often.</summary>
    public static readonly TimeSpan RetryPicture = TimeSpan.FromDays(1);

    /// <summary>Everything known — refreshed this often: people change names and pictures.</summary>
    public static readonly TimeSpan Refresh = TimeSpan.FromDays(7);

    public static bool HasProfileApi(ChannelType channelType) => channelType is ChannelType.Instagram or ChannelType.Facebook;

    /// <summary>Instagram: no @username yet; Facebook (which has no username): no name yet.</summary>
    public static bool IsUnknown(Conversation conversation, ChannelType channelType) =>
        channelType == ChannelType.Instagram ? conversation.ContactUsername is null : conversation.ContactName is null;

    /// <summary>A message from the person arrived: is it time to ask Meta again?</summary>
    public static bool ShouldAskOnMessage(Conversation conversation, ChannelType channelType, DateTimeOffset now)
    {
        if (!HasProfileApi(channelType))
            return false;
        if (conversation.ContactProfileFetchedAt is not { } lastAsked)
            return true;

        var since = now - lastAsked;
        if (IsUnknown(conversation, channelType))
            return since >= RetryUnknown;
        if (conversation.ContactAvatarPath is null)
            return since >= RetryPicture;
        return since >= Refresh;
    }
}
