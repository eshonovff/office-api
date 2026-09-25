namespace Office.Api.Data.Entities;

/// <summary>
/// A comment on a post of a connected Instagram account — a fan's, or the account's own (a reply
/// typed in the Instagram app, by the мизоҷ on the comments page, or by an automation). Stored
/// from webhooks and from on-demand syncs of a post. Channel-owned: the tenant filter shows a
/// мизоҷ only their own channels' comments (AppDbContext), and it is erased with the channel.
/// </summary>
public class InstagramComment
{
    public Guid Id { get; set; }

    public Guid ChannelId { get; set; }
    public Channel Channel { get; set; } = null!;

    /// <summary>Meta's comment id — unique within the channel.</summary>
    public required string ExternalId { get; set; }

    /// <summary>The post (Meta media id) the comment is on.</summary>
    public required string MediaExternalId { get; set; }

    /// <summary>Set when this comment answers another one (a thread).</summary>
    public string? ParentExternalId { get; set; }

    public required string AuthorExternalId { get; set; }
    public string? AuthorUsername { get; set; }
    public required string Text { get; set; }

    public DateTimeOffset CommentedAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>Written by the connected account itself, not by a fan.</summary>
    public bool IsOwn { get; set; }

    /// <summary>An own reply posted by an automation (shown with 🤖), not typed by the мизоҷ.</summary>
    public bool PostedByAutomation { get; set; }

    public bool IsHidden { get; set; }

    /// <summary>
    /// When a Direct message was sent from this comment (Meta allows one per comment, within 7
    /// days) — by an automation or by hand. Claimed atomically before the call to Meta.
    /// </summary>
    public DateTimeOffset? PrivateReplySentAt { get; set; }

    public bool IsRead { get; set; }

    /// <summary>Why the automated public reply to this comment failed, if it did.</summary>
    public string? AutoReplyError { get; set; }
}
