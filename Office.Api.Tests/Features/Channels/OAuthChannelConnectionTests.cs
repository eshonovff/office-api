using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Channels;
using Office.Api.Channels.Meta;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Features.Channels;

namespace Office.Api.Tests.Features.Channels;

/// <summary>
/// "One account, one owner". WhatsApp is used as the channel type so SaveAsync makes no Meta
/// webhook-subscription calls (those only run for Facebook/Instagram) — the ownership logic is
/// the same for every type. Each save runs on a context filtered to the caller, like in a request.
/// </summary>
public class OAuthChannelConnectionTests
{
    private static readonly Guid MizojA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid MizojB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private const string ExternalId = "acc-1";

    private sealed class FixedTenant(TenantIdentity identity) : ITenantContext
    {
        public TenantIdentity Current => identity;
    }

    private sealed class PlainProtector : IChannelCredentialsProtector
    {
        public string Protect(string plainText) => "enc:" + plainText;
        public string Unprotect(string protectedText) => protectedText["enc:".Length..];
    }

    private readonly string _databaseName = Guid.NewGuid().ToString();

    private AppDbContext Open(TenantIdentity? tenant = null) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_databaseName).Options,
        tenant is null ? null : new FixedTenant(tenant.Value));

    private static TenantIdentity As(Guid? mizoj) =>
        mizoj is null ? new(TenantScope.Staff, null) : new(TenantScope.Customer, mizoj);

    private void SeedExisting(Guid? owner, bool isActive = true)
    {
        using var db = Open();
        db.Channels.Add(new Channel
        {
            Id = Guid.NewGuid(), Type = ChannelType.WhatsApp, Name = "existing", ExternalId = ExternalId,
            CustomerId = owner, IsActive = isActive, CredentialsEncrypted = "enc:old",
        });
        db.SaveChanges();
    }

    private async Task<ChannelConnectResult> Connect(Guid? asOwner)
    {
        await using var db = Open(As(asOwner));
        return await OAuthChannelConnection.SaveAsync(
            ChannelType.WhatsApp, new ConnectableAccount(ExternalId, "Acc", "new-credentials"), "renamed", asOwner,
            db, new PlainProtector(), facebookConnector: null!, instagramConnector: null!, CancellationToken.None);
    }

    private Channel TheOnlyChannel()
    {
        using var db = Open();
        return Assert.Single(db.Channels.ToList());
    }

    [Fact]
    public async Task NewAccount_IsCreatedForTheCaller()
    {
        var result = await Connect(MizojA);

        Assert.Equal(ChannelConnectOutcome.Created, result.Outcome);
        Assert.Equal(MizojA, TheOnlyChannel().CustomerId);
    }

    [Fact]
    public async Task MizojCannotTakeACompanyAccount()
    {
        SeedExisting(owner: null);

        var result = await Connect(MizojA);

        Assert.Equal(ChannelConnectOutcome.OwnedByAnother, result.Outcome);
        var channel = TheOnlyChannel();
        Assert.Null(channel.CustomerId);
        Assert.Equal("enc:old", channel.CredentialsEncrypted); // untouched
    }

    [Fact]
    public async Task MizojCannotTakeAnotherMizojsAccount()
    {
        SeedExisting(MizojA);

        Assert.Equal(ChannelConnectOutcome.OwnedByAnother, (await Connect(MizojB)).Outcome);
        Assert.Equal(MizojA, TheOnlyChannel().CustomerId);
    }

    [Fact]
    public async Task StaffGetsAConflict_NotACrash_ForAMizojsAccount()
    {
        // The staff tenant filter hides the мизоҷ's channel; without IgnoreQueryFilters the lookup
        // missed it and the insert then broke the unique (Type, ExternalId) index.
        SeedExisting(MizojA);

        Assert.Equal(ChannelConnectOutcome.OwnedByAnother, (await Connect(asOwner: null)).Outcome);
        Assert.Equal(MizojA, TheOnlyChannel().CustomerId);
    }

    [Fact]
    public async Task OwnerReconnecting_ReactivatesAndRefreshesTheirChannel()
    {
        SeedExisting(MizojA, isActive: false);

        var result = await Connect(MizojA);

        Assert.Equal(ChannelConnectOutcome.Updated, result.Outcome);
        var channel = TheOnlyChannel();
        Assert.True(channel.IsActive);
        Assert.Equal("enc:new-credentials", channel.CredentialsEncrypted);
        Assert.Equal(MizojA, channel.CustomerId);
    }
}
