namespace Office.Api.Features.CustomerBroadcasts;

/// <param name="Kind">"message" or "flow".</param>
/// <param name="NotReachable">Of the audience, those whose window was closed (or who had a broadcast that day) when sending began.</param>
public record BroadcastListItem(
    Guid Id,
    Guid ChannelId,
    string Name,
    string Status,
    string Kind,
    DateTimeOffset ScheduledAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    int Recipients,
    int Sent,
    int Failed,
    int Skipped,
    int NotReachable,
    string? Error);

public record BroadcastFailure(Guid ContactId, string? Name, string? Username, string? Error);

public record BroadcastDetail(
    BroadcastListItem Summary,
    IReadOnlyList<string> Tags,
    string? Text,
    string? MediaPreviewDataUri,
    string? ButtonTitle,
    string? ButtonUrl,
    Guid? FlowId,
    string? FlowName,
    IReadOnlyList<BroadcastFailure> Failures);

/// <param name="Audience">Contacts of the channel (with one of the tags, if any).</param>
/// <param name="Reachable">Of them, who a broadcast would reach right now.</param>
public record BroadcastAudience(int Audience, int Reachable);

/// <summary>
/// A message (Text and/or an image, an optional link button) or a flow — never both.
/// ScheduledAt null = now.
/// </summary>
public record CreateBroadcastRequest(
    Guid ChannelId,
    string Name,
    string[]? Tags,
    string? Text,
    string? MediaId,
    string? MediaPreviewDataUri,
    string? ButtonTitle,
    string? ButtonUrl,
    Guid? FlowId,
    DateTimeOffset? ScheduledAt);
