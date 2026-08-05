using Microsoft.AspNetCore.SignalR;

namespace Office.Api.Realtime;

public interface IBoardEventPublisher
{
    Task TaskCreatedAsync(Guid projectId, object payload, CancellationToken ct);
    Task TaskMovedAsync(Guid projectId, object payload, CancellationToken ct);
    Task TaskUpdatedAsync(Guid projectId, object payload, CancellationToken ct);
    Task TaskDeletedAsync(Guid projectId, object payload, CancellationToken ct);
    Task CommentAddedAsync(Guid projectId, object payload, CancellationToken ct);
}

public class BoardEventPublisher(IHubContext<BoardHub> hubContext) : IBoardEventPublisher
{
    public Task TaskCreatedAsync(Guid projectId, object payload, CancellationToken ct) =>
        SendAsync(projectId, "TaskCreated", payload, ct);

    public Task TaskMovedAsync(Guid projectId, object payload, CancellationToken ct) =>
        SendAsync(projectId, "TaskMoved", payload, ct);

    public Task TaskUpdatedAsync(Guid projectId, object payload, CancellationToken ct) =>
        SendAsync(projectId, "TaskUpdated", payload, ct);

    public Task TaskDeletedAsync(Guid projectId, object payload, CancellationToken ct) =>
        SendAsync(projectId, "TaskDeleted", payload, ct);

    public Task CommentAddedAsync(Guid projectId, object payload, CancellationToken ct) =>
        SendAsync(projectId, "CommentAdded", payload, ct);

    private Task SendAsync(Guid projectId, string eventName, object payload, CancellationToken ct) =>
        hubContext.Clients.Group(BoardHub.GroupName(projectId)).SendAsync(eventName, payload, ct);
}
