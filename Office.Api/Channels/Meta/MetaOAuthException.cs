namespace Office.Api.Channels.Meta;

/// <summary>
/// Хатои Meta OAuth/Graph API дар вақти табдили code ба token ё дархости минбаъда.
/// Статус ва матни ПУРРАИ хатогиро мебардорад (на танҳо статус-код), то
/// ChannelOAuthEndpoints.CallbackAsync онро ҳам пурра log, ҳам ба корбар (тавассути
/// popup-и OAuthPostMessagePage) нишон дода тавонад — бе ин, хато ба
/// "муваффақ нашуд"-и умумӣ монда, сабаби воқеӣ (масалан redirect_uri номувофиқ,
/// app secret нодуруст) на дар лог, на дар UI намоён намешуд.
/// </summary>
public class MetaOAuthException(string context, int statusCode, string responseBody)
    : Exception($"{context}: HTTP {statusCode} — {responseBody}")
{
    public string Context { get; } = context;
    public int StatusCode { get; } = statusCode;
    public string ResponseBody { get; } = responseBody;
}
