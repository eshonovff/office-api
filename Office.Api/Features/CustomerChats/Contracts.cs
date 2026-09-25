using FluentValidation;

namespace Office.Api.Features.CustomerChats;

/// <summary>
/// A chat as a мизоҷ sees it. Deliberately smaller than the staff ConversationListItem: no
/// assignee (a мизоҷ has no team yet) and no provider ids (ExternalId) — nothing the page needs.
/// </summary>
public record CustomerConversationListItem(
    Guid Id,
    Guid ChannelId,
    string ChannelType,
    string ChannelName,
    string? ContactName,
    string? ContactAvatarUrl,
    string? ContactUsername,
    DateTimeOffset? LastMessageAt,
    CustomerLastMessage? LastMessage,
    int UnreadCount,
    DateTimeOffset? WindowExpiresAt,
    DateTimeOffset CreatedAt);

/// <summary>The newest message of a chat, for the one-line preview in the list.</summary>
public record CustomerLastMessage(string Type, string Direction, string? Body);

/// <param name="CanSend">
/// False when the plan has run out: the history stays readable, replying needs a plan.
/// </param>
/// <param name="ChannelNeedsReconnect">The Instagram connection must be renewed before replying.</param>
public record CustomerConversationDetail(
    Guid Id,
    Guid ChannelId,
    string ChannelType,
    string ChannelName,
    string? ContactName,
    string? ContactAvatarUrl,
    string? ContactUsername,
    DateTimeOffset? LastMessageAt,
    int UnreadCount,
    DateTimeOffset? WindowExpiresAt,
    DateTimeOffset CreatedAt,
    bool CanSend,
    bool ChannelNeedsReconnect);

public record SendCustomerMessageRequest(string Body);

public record UnreadChatsResponse(int Count);

public class SendCustomerMessageRequestValidator : AbstractValidator<SendCustomerMessageRequest>
{
    // Instagram's own limit for a text message.
    public const int MaxLength = 1000;

    public SendCustomerMessageRequestValidator()
    {
        RuleFor(x => x.Body)
            .Must(body => !string.IsNullOrWhiteSpace(body)).WithMessage("Паём холӣ аст.")
            .MaximumLength(MaxLength).WithMessage($"Паём набояд аз {MaxLength} аломат зиёд бошад.");
    }
}
