namespace Office.Api.Features.Channels;

public record ChannelMemberDto(Guid UserId, string FullName, string Username);

public record ChannelListItem(Guid Id, string Type, string Name, string ExternalId, bool IsActive, DateTimeOffset CreatedAt);

/// <summary>
/// GET /api/channels/mine — барои inbox.view, на channels.manage; ExternalId/CreatedAt-ро
/// намебарорад. Joinable = оё frontend бояд channel:{id}-и SignalR-ро бипайвандад — барои
/// only_assigned операторон false аст, чунки онҳо навсозиро тавассути user:{id} мегиранд.
/// </summary>
public record ChannelSummary(Guid Id, string Type, string Name, bool IsActive, bool Joinable);

public record ChannelDetail(
    Guid Id,
    string Type,
    string Name,
    string ExternalId,
    bool IsActive,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ChannelMemberDto> Members);

public record CreateChannelRequest(string Type, string Name, string ExternalId, string Credentials);

public record UpdateChannelRequest(string Name, string? Credentials, bool IsActive);

public record SetChannelMembersRequest(IReadOnlyList<Guid> UserIds);

/// <summary>
/// GET /{id}/assignable-users, /conversations/{id}/assignable-users — на танҳо channel_members:
/// Owner/Admin низ дохил мешаванд (онҳо бе узвияти расмӣ ҳам ба ҳар сӯҳбат дастрасӣ доранд,
/// пас IChannelAccessGuard.CanUserBeAssignedToChannelAsync онҳоро таъин иҷозат медиҳад).
/// </summary>
public record AssignableUserDto(Guid UserId, string FullName, string Username);
