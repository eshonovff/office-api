using Office.Api.Channels.Meta;
using Office.Api.Data.Entities;

namespace Office.Api.Tests.Channels.Meta;

public class OAuthConnectPolicyTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly ConnectableAccount Account = new("page-1", "My Page", "{}");

    private static OAuthConnectionSession MakeSession(ChannelType provider, Guid userId) =>
        new(provider, userId, DateTimeOffset.UtcNow.AddMinutes(10), [Account]);

    [Fact]
    public void ResolveAccount_MatchingProviderUserAndAccount_ReturnsAccount()
    {
        var session = MakeSession(ChannelType.Facebook, UserId);

        var account = OAuthConnectPolicy.ResolveAccount(session, ChannelType.Facebook, UserId, "page-1");

        Assert.Same(Account, account);
    }

    [Fact]
    public void ResolveAccount_WrongProvider_ReturnsNull()
    {
        var session = MakeSession(ChannelType.Facebook, UserId);

        var account = OAuthConnectPolicy.ResolveAccount(session, ChannelType.Instagram, UserId, "page-1");

        Assert.Null(account);
    }

    [Fact]
    public void ResolveAccount_WrongUser_ReturnsNull()
    {
        var session = MakeSession(ChannelType.Facebook, UserId);

        var account = OAuthConnectPolicy.ResolveAccount(session, ChannelType.Facebook, OtherUserId, "page-1");

        Assert.Null(account);
    }

    [Fact]
    public void ResolveAccount_UnknownExternalId_ReturnsNull()
    {
        var session = MakeSession(ChannelType.Facebook, UserId);

        var account = OAuthConnectPolicy.ResolveAccount(session, ChannelType.Facebook, UserId, "page-does-not-exist");

        Assert.Null(account);
    }
}
