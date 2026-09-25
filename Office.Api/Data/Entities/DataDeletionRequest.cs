namespace Office.Api.Data.Entities;

public enum DataDeletionStatus
{
    Pending,
    Completed,
}

/// <summary>
/// One data-deletion request from Meta (a user asked Meta to delete what our app holds about
/// them). Not tied to a channel by foreign key on purpose — the channels it matches are exactly
/// what gets deleted — so it is not tenant-filtered either; only DataDeletionJob and the public
/// status page (by unguessable code) ever read it.
/// </summary>
public class DataDeletionRequest
{
    public Guid Id { get; set; }

    /// <summary>"instagram" — which Meta app sent it.</summary>
    public required string Provider { get; set; }

    /// <summary>The user id from Meta's signed payload. Cleared once processed (kept no longer than needed).</summary>
    public string? MetaUserId { get; set; }

    /// <summary>Handed back to Meta; the user checks status with it. 128 random bits, lowercase hex.</summary>
    public required string ConfirmationCode { get; set; }

    /// <summary>SHA-256 of the signed_request — the same request delivered twice is processed once.</summary>
    public required string SignedRequestHash { get; set; }

    public DataDeletionStatus Status { get; set; }
    public int ChannelsDeleted { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
