using Office.Api.Data.Entities;

namespace Office.Api.Channels.Meta;

public sealed record OAuthConnectionSession(
    ChannelType Provider, Guid InitiatedByUserId, DateTimeOffset ExpiresAt, IReadOnlyList<ConnectableAccount> Accounts);
