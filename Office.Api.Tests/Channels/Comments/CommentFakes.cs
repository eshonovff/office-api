using Office.Api.Channels.Comments;

namespace Office.Api.Tests.Channels.Comments;

public sealed class RecordingPublicReplyScheduler : ICommentPublicReplyScheduler
{
    public List<(Guid ChannelId, string CommentId, string Text)> Scheduled { get; } = [];

    public void Schedule(Guid channelId, string commentExternalId, string text) =>
        Scheduled.Add((channelId, commentExternalId, text));
}

public sealed class RecordingCommentEventPublisher : ICommentEventPublisher
{
    public List<(Guid ChannelId, string MediaId)> Changed { get; } = [];

    public Task CommentsChangedAsync(Guid channelId, string mediaExternalId, CancellationToken ct)
    {
        Changed.Add((channelId, mediaExternalId));
        return Task.CompletedTask;
    }
}
