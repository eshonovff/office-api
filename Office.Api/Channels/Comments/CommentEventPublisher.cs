using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Office.Api.Data;
using Office.Api.Realtime;

namespace Office.Api.Channels.Comments;

public interface ICommentEventPublisher
{
    Task CommentsChangedAsync(Guid channelId, string mediaExternalId, CancellationToken ct);
}

/// <summary>
/// "The comments of this post changed" — to the channel's мизоҷ only, on CustomerHub, and with no
/// comment text: the page re-reads through /api/public, where the tenant filter applies. Company
/// channels have no comments page yet, so nothing is sent for them.
/// </summary>
public class CommentEventPublisher(IHubContext<CustomerHub> customerHub, AppDbContext db) : ICommentEventPublisher
{
    public const string CommentsUpdatedEvent = "CommentsUpdated";

    public async Task CommentsChangedAsync(Guid channelId, string mediaExternalId, CancellationToken ct)
    {
        // Called from webhooks and jobs (no user) and from мизоҷ requests — the owner is read past
        // the tenant filter, by this channel's own id only.
        var ownerId = await db.Channels.IgnoreQueryFilters()
            .Where(c => c.Id == channelId)
            .Select(c => c.CustomerId)
            .FirstOrDefaultAsync(ct);
        if (ownerId is null)
            return;

        await customerHub.Clients.Group(CustomerHub.CustomerGroupName(ownerId.Value))
            .SendAsync(CommentsUpdatedEvent, new { channelId, mediaId = mediaExternalId }, ct);
    }
}
