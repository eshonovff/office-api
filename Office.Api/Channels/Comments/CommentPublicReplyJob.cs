using Hangfire;
using Microsoft.EntityFrameworkCore;
using Office.Api.Channels.Flows;
using Office.Api.Channels.Instagram;
using Office.Api.Data;
using Office.Api.Data.Entities;

namespace Office.Api.Channels.Comments;

public interface ICommentPublicReplyScheduler
{
    void Schedule(Guid channelId, string commentExternalId, string text);
}

/// <summary>A short random delay (5–20 s): an instant identical-looking reply is what spam looks like.</summary>
public class CommentPublicReplyScheduler(IBackgroundJobClient jobs) : ICommentPublicReplyScheduler
{
    public void Schedule(Guid channelId, string commentExternalId, string text) =>
        jobs.Schedule<CommentPublicReplyJob>(
            j => j.RunAsync(channelId, commentExternalId, text, CancellationToken.None),
            TimeSpan.FromSeconds(Random.Shared.Next(5, 21)));
}

/// <summary>
/// An automation's public reply under a comment (flow trigger "publicReplies"). Everything is
/// re-checked at run time — the plan may have ended, the account been disconnected or the comment
/// hidden in the seconds since it was scheduled. Never retried: a retry after a reply that did go
/// out would post it twice. A failure is kept on the comment so the мизоҷ sees it.
/// </summary>
[AutomaticRetry(Attempts = 0)]
public class CommentPublicReplyJob(
    AppDbContext db,
    InstagramProvider instagram,
    ICommentEventPublisher events,
    ILogger<CommentPublicReplyJob> logger)
{
    public async Task RunAsync(Guid channelId, string commentExternalId, string text, CancellationToken ct)
    {
        var channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == channelId, ct);
        if (channel is null || !channel.IsActive || channel.RequiresReconnect || !await AutomationRunGate.CanRunAsync(channelId, db, ct))
            return;

        var comment = await db.InstagramComments.FirstOrDefaultAsync(c => c.ChannelId == channelId && c.ExternalId == commentExternalId, ct);
        if (comment?.IsHidden == true)
            return; // hidden by the мизоҷ meanwhile — no reply under it

        // Instagram threads are one level deep: a reply to a reply goes under the top comment.
        var target = comment?.ParentExternalId ?? commentExternalId;
        try
        {
            var replyId = await instagram.ReplyToCommentAsync(channel, target, text, ct);
            if (comment is not null)
            {
                comment.AutoReplyError = null;
                if (replyId is not null)
                    await RecordOwnReplyAsync(channel, comment, target, replyId, text, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The text of a comment never goes to the log — only ids and Meta's reason.
            logger.LogWarning(ex, "Public reply to comment {CommentId} on channel {ChannelId} failed", commentExternalId, channelId);
            if (comment is not null)
            {
                var reason = ex is GraphApiException ? ex.Message : "Instagram ҷавобро қабул накард.";
                comment.AutoReplyError = reason.Length > 500 ? reason[..500] : reason;
            }
        }

        await db.SaveChangesAsync(ct);
        if (comment is not null)
            await events.CommentsChangedAsync(channelId, comment.MediaExternalId, ct);
    }

    /// <summary>
    /// Stores the reply as the account's own comment, marked 🤖. Its webhook echo may have been
    /// stored first (as a plain own comment) — then that row just gets the mark.
    /// </summary>
    private async Task RecordOwnReplyAsync(
        Channel channel, InstagramComment parent, string parentId, string replyId, string text, CancellationToken ct)
    {
        var echo = await db.InstagramComments.FirstOrDefaultAsync(c => c.ChannelId == channel.Id && c.ExternalId == replyId, ct);
        if (echo is not null)
        {
            echo.PostedByAutomation = true;
            return;
        }

        var now = DateTimeOffset.UtcNow;
        db.InstagramComments.Add(new InstagramComment
        {
            Id = Guid.CreateVersion7(),
            ChannelId = channel.Id,
            ExternalId = replyId,
            MediaExternalId = parent.MediaExternalId,
            ParentExternalId = parentId,
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
