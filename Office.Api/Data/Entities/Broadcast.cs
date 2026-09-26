namespace Office.Api.Data.Entities;

public enum BroadcastStatus
{
    Scheduled,
    Sending,
    Finished,
    Cancelled,
    Failed,
}

public enum BroadcastRecipientStatus
{
    Pending,
    Sent,
    Failed,
    /// <summary>The 24-hour window had closed by the time their turn came.</summary>
    SkippedWindowClosed,
    /// <summary>They already got a broadcast in the last 24 hours (one per person per day).</summary>
    SkippedRecentlyMessaged,
    /// <summary>The broadcast was stopped before their turn.</summary>
    Cancelled,
}

/// <summary>
/// A мизоҷ's broadcast on one of their channels (phase 18): a message — text, an image, a link
/// button — or a flow to start, sent to the contacts of THAT channel whose 24-hour window is
/// open, optionally only those with one of the chosen tags. The send job runs without a tenant
/// filter, so every query it makes is by this broadcast's own ChannelId.
/// </summary>
public class Broadcast
{
    public Guid Id { get; set; }
    public Guid ChannelId { get; set; }
    public Channel Channel { get; set; } = null!;
    public required string Name { get; set; }

    /// <summary>JSON array of tags; empty = every contact of the channel.</summary>
    public string TagsJson { get; set; } = "[]";

    public string? Text { get; set; }
    /// <summary>Meta's attachment id of an image uploaded for this channel (the flows' media upload).</summary>
    public string? MediaId { get; set; }
    public string? MediaPreviewDataUri { get; set; }
    public string? ButtonTitle { get; set; }
    public string? ButtonUrl { get; set; }

    /// <summary>Instead of a message: start this flow (same channel, active) for each recipient.</summary>
    public Guid? FlowId { get; set; }
    public Flow? Flow { get; set; }

    public BroadcastStatus Status { get; set; } = BroadcastStatus.Scheduled;
    public DateTimeOffset ScheduledAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Contacts of the audience (the tags) when sending began, reachable or not.</summary>
    public int AudienceCount { get; set; }
    /// <summary>Set by "stop": the next batch cancels whoever has not been sent to yet.</summary>
    public bool StopRequested { get; set; }
    public string? Error { get; set; }
    public string? JobId { get; set; }

    public ICollection<BroadcastRecipient> Recipients { get; set; } = [];
}

/// <summary>One person of a broadcast — one row each, so a retried batch never sends twice.</summary>
public class BroadcastRecipient
{
    public Guid BroadcastId { get; set; }
    public Broadcast Broadcast { get; set; } = null!;
    public Guid ContactId { get; set; }
    public Conversation Contact { get; set; } = null!;
    public BroadcastRecipientStatus Status { get; set; } = BroadcastRecipientStatus.Pending;
    public string? Error { get; set; }
    public DateTimeOffset? SentAt { get; set; }
}
