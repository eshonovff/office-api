using Microsoft.EntityFrameworkCore;
using Office.Api.Data;
using Office.Api.Data.Entities;

namespace Office.Api.Channels.Comments;

/// <summary>What was done from a stored comment, recorded where the comments page reads it.</summary>
public static class CommentLedger
{
    /// <summary>
    /// An automation sent the Direct message for this comment: marks it so the page offers no
    /// second one (Meta allows one per comment). Tracked, not saved — the caller's SaveChanges
    /// persists it with the rest of its work. A comment that was never stored (no post id) is skipped.
    /// </summary>
    public static async Task MarkPrivateReplySentAsync(
        AppDbContext db, Guid channelId, string commentExternalId, DateTimeOffset now, CancellationToken ct)
    {
        var comment = await db.InstagramComments.FirstOrDefaultAsync(c => c.ChannelId == channelId && c.ExternalId == commentExternalId, ct);
        if (comment is not null && comment.PrivateReplySentAt is null)
            comment.PrivateReplySentAt = now;
    }

    /// <summary>
    /// An automation's public reply went out: stored as the account's own comment, marked 🤖.
    /// Its webhook echo may have been stored first (as a plain own comment) — then that row just
    /// gets the mark. Tracked, not saved, like the above.
    /// </summary>
    public static async Task RecordAutomatedReplyAsync(
        AppDbContext db, Channel channel, InstagramComment parent, string parentExternalId, string replyExternalId, string text,
        DateTimeOffset now, CancellationToken ct)
    {
        var echo = await db.InstagramComments.FirstOrDefaultAsync(c => c.ChannelId == channel.Id && c.ExternalId == replyExternalId, ct);
        if (echo is not null)
        {
            echo.PostedByAutomation = true;
            return;
        }

        db.InstagramComments.Add(new InstagramComment
        {
            Id = Guid.CreateVersion7(),
            ChannelId = channel.Id,
            ExternalId = replyExternalId,
            MediaExternalId = parent.MediaExternalId,
            ParentExternalId = parentExternalId,
            AuthorExternalId = channel.ExternalId,
            AuthorUsername = channel.Name,
            Text = text,
            CommentedAt = now,
            ReceivedAt = now,
            IsOwn = true,
            PostedByAutomation = true,
            IsRead = true,
        });
    }
}
