namespace Office.Api.Features.Channels;

public record OAuthStartResponse(string Url);

/// <summary>Account (Page/IG business) барои интихоб — ҳеҷ токен дар ин ҷо нест.</summary>
public record OAuthAccountOption(string ExternalId, string Name);

public record OAuthCallbackResponse(Guid ConnectionId, IReadOnlyList<OAuthAccountOption> Accounts);
