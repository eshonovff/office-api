namespace Office.Api.Channels.Meta;

/// <summary>
/// Пайвасти канал тавассути OAuth (Meta) — фарқ аз <see cref="IChannelProvider"/>:
/// ин интерфейс дар бораи "чӣ гуна канал сохта мешавад" аст, на "чӣ гуна паём фиристода мешавад".
/// Танҳо Instagram/Facebook доранд — WhatsApp ҳамон тавре ки ҳаст (credentials дастӣ) мемонад.
/// </summary>
public interface IChannelOAuthConnector
{
    /// <summary>URL-и саҳифаи авторизатсияи Meta, бо state-и аллакай имзошуда.</summary>
    string BuildAuthorizationUrl(string redirectUri, string state);

    /// <summary>
    /// Табдили `code` ба token (кӯтоҳмуддат → дарозмуддат) ва рӯйхати account (Page/IG business)-е
    /// ки ин токен ба он дастрасӣ дорад — то оператор кадомашро пайваст кардан интихоб кунад.
    /// </summary>
    Task<IReadOnlyList<ConnectableAccount>> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct);
}
