namespace Office.Api.Features.CustomerContacts;

public record ContactVariableDto(string Key, string Value);

/// <param name="CanMessageUntil">
/// The end of the 24-hour window if it is open now (a message can be sent until then), else null.
/// </param>
public record CustomerContactListItem(
    Guid Id,
    Guid ChannelId,
    string ChannelType,
    string ChannelName,
    string? Name,
    string? Username,
    string? AvatarUrl,
    IReadOnlyList<string> Tags,
    IReadOnlyList<ContactVariableDto> Variables,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset? LastMessageAt,
    DateTimeOffset? CanMessageUntil);

public record ContactAutomationDto(Guid FlowId, string FlowName, string Status, DateTimeOffset StartedAt);

/// <param name="FollowStatus">The last follow check of the comment auto-reply: Following, NotFollowing, Unknown — or null if never checked.</param>
public record CustomerContactDetail(
    Guid Id,
    Guid ChannelId,
    string ChannelType,
    string ChannelName,
    string? Name,
    string? Username,
    string? AvatarUrl,
    IReadOnlyList<string> Tags,
    IReadOnlyList<ContactVariableDto> Variables,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset? LastMessageAt,
    DateTimeOffset? CanMessageUntil,
    int MessageCount,
    int CommentCount,
    string? FollowStatus,
    DateTimeOffset? FollowCheckedAt,
    IReadOnlyList<ContactAutomationDto> Automations);

public record ContactTagCount(string Tag, int Count);

public record AddContactTagRequest(string Tag);

public record SetContactVariableRequest(string Key, string Value);
