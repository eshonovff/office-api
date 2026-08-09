using Microsoft.AspNetCore.SignalR;

namespace Office.Api.Realtime;

public interface IInboxEventPublisher
{
    Task MessageReceivedAsync(Guid channelId, Guid? assignedTo, object payload, CancellationToken ct);
    Task MessageSentAsync(Guid channelId, Guid? assignedTo, object payload, CancellationToken ct);
    Task ConversationAssignedAsync(Guid channelId, Guid? assignedTo, object payload, CancellationToken ct);
    Task ConversationStatusChangedAsync(Guid channelId, Guid? assignedTo, object payload, CancellationToken ct);
}

/// <summary>Тибқи 6.18: ба гурӯҳи `channel:{id}` ҳамеша, ва илова ба `user:{assignedTo}` агар таъин шуда бошад.</summary>
public class InboxEventPublisher(IHubContext<InboxHub> hubContext) : IInboxEventPublisher
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
    }
}
