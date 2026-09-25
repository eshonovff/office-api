using Microsoft.EntityFrameworkCore;
using Office.Api.Channels.Instagram;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Realtime;

namespace Office.Api.Channels.Comments;

/// <summary>
/// Keeps the comments the webhooks deliver (and syncs fetch), once each: the (channel, Meta id)
/// pair is unique, so a repeated delivery or a sync overlapping a webhook changes nothing. The
/// channel always comes from the caller — the webhook entry it arrived in — never from the payload.
/// </summary>
public class CommentStore(AppDbContext db, ICommentEventPublisher events, ILogger<CommentStore> logger)
{
    /// <returns>True when the comment was new.</returns>
    public async Task<bool> RecordAsync(Channel channel, ParsedCommentEvent evt, CancellationToken ct)
    {
        // A comment is always on a post; without one there is nowhere to show it (automations still run).
        if (string.IsNullOrEmpty(evt.MediaId))
            return false;

        if (await db.InstagramComments.AnyAsync(c => c.ChannelId == channel.Id && c.ExternalId == evt.CommentId, ct))
            return false;

        var now = DateTimeOffset.UtcNow;
        var isOwn = evt.ActorExternalId == channel.ExternalId;
        var comment = new InstagramComment
        {
            Id = Guid.CreateVersion7(),
            ChannelId = channel.Id,
            ExternalId = evt.CommentId,
            MediaExternalId = evt.MediaId,
            ParentExternalId = evt.ParentId,
            AuthorExternalId = evt.ActorExternalId,
            AuthorUsername = evt.ActorUsername,
            Text = evt.Text,
            CommentedAt = evt.OccurredAt ?? now,
            ReceivedAt = now,
            IsOwn = isOwn,
            // The account's own comments are never "new" to it.
            IsRead = isOwn,
        };
        db.InstagramComments.Add(comment);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two deliveries of the same comment raced past the check; the unique index kept one.
            db.Entry(comment).State = EntityState.Detached;
            logger.LogInformation("Comment {CommentId} on channel {ChannelId} was already stored", evt.CommentId, channel.Id);
            return false;
        }

        await events.CommentsChangedAsync(channel.Id, evt.MediaId, ct);
        return true;
    }
}
