using Microsoft.EntityFrameworkCore;
using Office.Api.Data;

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
}
