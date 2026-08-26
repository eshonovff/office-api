using Office.Api.Data.Entities;

namespace Office.Api.Channels.Meta;

/// <summary>
/// Кадом account аз session-и OAuth барои /connect иҷозат дорад — pure, бе DB.
/// Session бояд ба ҳамон provider тааллуқ дошта бошад ва ба ҳамон корбаре, ки OAuth-ро
/// оғоз кардааст (ниг. UserId-и state дар <see cref="OAuthStatePayload"/>) — то connectionId,
/// агар лаффида/дуздида шавад, аз ҷониби корбари дигар истифода нашавад.
/// </summary>
public static class OAuthConnectPolicy
{
    public static ConnectableAccount? ResolveAccount(
        OAuthConnectionSession session, ChannelType requestedProvider, Guid callerUserId, string externalId)
    {
        if (session.Provider != requestedProvider || session.InitiatedByUserId != callerUserId)
            return null;

        return session.Accounts.FirstOrDefault(a => a.ExternalId == externalId);
    }
}
