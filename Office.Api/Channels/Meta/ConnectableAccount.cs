namespace Office.Api.Channels.Meta;

/// <summary>
/// Account (Page-и Facebook / account-и бизнеси Instagram)-е, ки корбар метавонад пайваст кунад.
/// CredentialsJson танҳо дар <see cref="OAuthConnectionStore"/> (дохили процесс) мемонад — ҳеҷ гоҳ
/// ба DTO-и берунии /callback намебарояд, то токен ба фронтенд/browser нарасад.
/// </summary>
public sealed record ConnectableAccount(
    string ExternalId,
    string Name,
    string CredentialsJson,
    /// <summary>Танҳо Instagram медиҳад (ig_exchange_token-и response's expires_in) — Facebook null мемонад.</summary>
    DateTimeOffset? CredentialsExpiresAt = null,
    /// <summary>
    /// Instagram only: the app-scoped user id (/me.id), which is NOT ExternalId (/me.user_id).
    /// Meta's data-deletion callback identifies the user by an id of its own choosing — kept so
    /// that request can be matched to this channel. Facebook: null.
    /// </summary>
    string? AppScopedUserId = null);
