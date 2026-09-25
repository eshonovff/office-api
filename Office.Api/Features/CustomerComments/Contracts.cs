using FluentValidation;

namespace Office.Api.Features.CustomerComments;

public record CustomerCommentPost(
    string MediaId,
    string? MediaType,
    string? ImageUrl,
    string? Permalink,
    string? Caption,
    string? Timestamp,
    int CommentCount,
    int NewCount);

public record CustomerCommentPostsResult(IReadOnlyList<CustomerCommentPost> Items, string? NextCursor);

/// <summary>
/// A comment as the мизоҷ sees it. Only our own ids go out — never Meta's comment or user ids —
/// so an action can only ever name a row the tenant filter lets this мизоҷ see.
/// </summary>
/// <param name="DirectAvailableUntil">Meta allows the one Direct message within 7 days of the comment.</param>
public record CustomerCommentDto(
    Guid Id,
    Guid? ParentId,
    string? AuthorUsername,
    bool IsOwn,
    bool PostedByAutomation,
    string Text,
    DateTimeOffset CommentedAt,
    bool IsHidden,
    bool IsRead,
    bool DirectSent,
    bool CanSendDirect,
    DateTimeOffset DirectAvailableUntil,
    string? AutoReplyError);

public record CommentTextRequest(string Text);

public record HideCommentRequest(bool Hidden);

public record CommentsOfPostRequest(Guid ChannelId, string MediaId);

public record CommentSyncResult(int Added, bool Throttled);

public record NewCommentsResponse(int Count);

public class CommentTextRequestValidator : AbstractValidator<CommentTextRequest>
{
    // Instagram's own limit for a Direct message; a public reply is kept to the same.
    public const int MaxLength = 1000;

    public CommentTextRequestValidator()
    {
        RuleFor(x => x.Text)
            .Must(text => !string.IsNullOrWhiteSpace(text)).WithMessage("Матн холӣ аст.")
            .MaximumLength(MaxLength).WithMessage($"Матн набояд аз {MaxLength} аломат зиёд бошад.");
    }
}

public class CommentsOfPostRequestValidator : AbstractValidator<CommentsOfPostRequest>
{
    public CommentsOfPostRequestValidator()
    {
        RuleFor(x => x.ChannelId).NotEmpty();
        // Meta media ids are digits; anything else is refused before it reaches a URL.
        RuleFor(x => x.MediaId).NotEmpty().MaximumLength(64).Matches("^[0-9_]+$");
    }
}
