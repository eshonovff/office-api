using Microsoft.EntityFrameworkCore;
using Office.Api.Data;
using Office.Api.Data.Entities;

namespace Office.Api.Channels.ContactProfiles;

/// <summary>Asks Meta who a contact is and writes it on the chat (not saved — the caller saves).</summary>
public static class ContactProfileUpdater
{
    /// <returns>True when there is a picture to fetch (ContactAvatarJob).</returns>
    public static async Task<bool> RefreshAsync(
        AppDbContext db, IChannelProvider provider, Channel channel, Conversation conversation, DateTimeOffset now, CancellationToken ct)
    {
        var profile = await provider.GetContactProfileAsync(channel, conversation.ExternalId, ct);
        conversation.ContactProfileFetchedAt = now;

        // What Meta says now wins — people rename themselves; nothing is erased when it says nothing.
        if (profile.Name is not null)
            conversation.ContactName = profile.Name;
        if (profile.Username is not null)
            conversation.ContactUsername = profile.Username;
        if (profile.AvatarUrl is not null)
            conversation.ContactAvatarUrl = profile.AvatarUrl;

        // The account wrote first (an auto-reply to a comment): Meta says nothing until the person
        // writes back, but their comment on this account already said who they are.
        if (channel.Type == ChannelType.Instagram && conversation.ContactUsername is null)
        {
            var fromComment = await db.InstagramComments
                .Where(c => c.ChannelId == channel.Id && c.AuthorExternalId == conversation.ExternalId && c.AuthorUsername != null)
                .OrderByDescending(c => c.CommentedAt)
                .Select(c => c.AuthorUsername)
                .FirstOrDefaultAsync(ct);
            if (fromComment is not null)
            {
                conversation.ContactUsername = fromComment;
                conversation.ContactName ??= fromComment;
            }
        }

        return profile.AvatarUrl is not null;
    }
}
