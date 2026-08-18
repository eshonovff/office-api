namespace Office.Api.Channels.Meta;

/// <summary>
/// Account (Page-и Facebook / account-и бизнеси Instagram)-е, ки корбар метавонад пайваст кунад.
/// CredentialsJson танҳо дар <see cref="OAuthConnectionStore"/> (дохили процесс) мемонад — ҳеҷ гоҳ
/// ба DTO-и берунии /callback намебарояд, то токен ба фронтенд/browser нарасад.
/// </summary>
public sealed record ConnectableAccount(string ExternalId, string Name, string CredentialsJson);
