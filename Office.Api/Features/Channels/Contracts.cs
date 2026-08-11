namespace Office.Api.Features.Channels;

public record ChannelMemberDto(Guid UserId, string FullName, string Username);

public record ChannelListItem(Guid Id, string Type, string Name, string ExternalId, bool IsActive, DateTimeOffset CreatedAt);

/// <summary>GET /api/channels/mine — барои inbox.view, на channels.manage; ExternalId/CreatedAt-ро намебарорад.</summary>
public record ChannelSummary(Guid Id, string Type, string Name, bool IsActive);

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
