using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Office.Api.Data;
using Office.Api.Features.Conversations;
using Office.Api.Features.CustomerChats;

namespace Office.Api.Realtime;

public interface IInboxEventPublisher
{
    Task MessageReceivedAsync(Guid channelId, Guid? assignedTo, object payload, CancellationToken ct);
    Task MessageSentAsync(Guid channelId, Guid? assignedTo, object payload, CancellationToken ct);
    Task ConversationAssignedAsync(Guid channelId, Guid? assignedTo, object payload, CancellationToken ct);
    Task ConversationStatusChangedAsync(Guid channelId, Guid? assignedTo, object payload, CancellationToken ct);
}

/// <summary>
/// Тибқи 6.18: ба гурӯҳи `channel:{id}` ҳамеша, ва илова ба `user:{assignedTo}` агар таъин шуда бошад.
/// For a мизоҷ's channel also a bare "ChatUpdated" {conversationId} to that мизоҷ on CustomerHub —
/// never the staff-shaped payload itself.
/// </summary>
public class InboxEventPublisher(
    IHubContext<InboxHub> hubContext,
    IHubContext<CustomerHub> customerHubContext,
    AppDbContext db) : IInboxEventPublisher
{
    public Task MessageReceivedAsync(Guid channelId, Guid? assignedTo, object payload, CancellationToken ct) =>
        SendAsync(channelId, assignedTo, "MessageReceived", payload, ct);

    public Task MessageSentAsync(Guid channelId, Guid? assignedTo, object payload, CancellationToken ct) =>
        SendAsync(channelId, assignedTo, "MessageSent", payload, ct);

    public Task ConversationAssignedAsync(Guid channelId, Guid? assignedTo, object payload, CancellationToken ct) =>
        SendAsync(channelId, assignedTo, "ConversationAssigned", payload, ct);

    public Task ConversationStatusChangedAsync(Guid channelId, Guid? assignedTo, object payload, CancellationToken ct) =>
        SendAsync(channelId, assignedTo, "ConversationStatusChanged", payload, ct);

    private async Task SendAsync(Guid channelId, Guid? assignedTo, string eventName, object payload, CancellationToken ct)
    {
        await hubContext.Clients.Group(InboxHub.ChannelGroupName(channelId)).SendAsync(eventName, payload, ct);

        if (assignedTo is not null)
            await hubContext.Clients.Group(InboxHub.UserGroupName(assignedTo.Value)).SendAsync(eventName, payload, ct);

        // Callers run as staff, as a мизоҷ, or with no user at all (webhooks, Hangfire), so the
        // owner is looked up past the tenant filter — by this channel's own id only.
        var ownerId = await db.Channels.IgnoreQueryFilters()
            .Where(c => c.Id == channelId)
            .Select(c => c.CustomerId)
            .FirstOrDefaultAsync(ct);
        if (ownerId is null)
            return;

        var conversationId = payload switch
        {
            MessageDto message => message.ConversationId,
            ConversationDetail detail => detail.Id,
            CustomerConversationDetail detail => detail.Id,
            _ => (Guid?)null,
        };
        await customerHubContext.Clients.Group(CustomerHub.CustomerGroupName(ownerId.Value))
            .SendAsync(CustomerHub.ChatUpdatedEvent, new { conversationId }, ct);
    }
}
