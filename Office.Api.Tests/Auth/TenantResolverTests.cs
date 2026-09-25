using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Office.Api.Auth;

namespace Office.Api.Tests.Auth;

public class TenantResolverTests
{
    private static readonly Guid CustomerId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static ClaimsIdentity Identity(string authenticationType, params Claim[] claims) =>
        new(claims, authenticationType);

    private static ClaimsPrincipal Principal(params ClaimsIdentity[] identities) => new(identities);

    [Fact]
    public void NoHttpRequest_IsSystem()
    {
        // Hangfire jobs and startup: the WebhookProcessor must route webhooks for every owner.
        Assert.Equal(new TenantIdentity(TenantScope.System, null), TenantResolver.Resolve(null, inHttpRequest: false));
    }

    [Fact]
    public void AnonymousRequest_IsNone()
    {
        Assert.Equal(TenantScope.None, TenantResolver.Resolve(new ClaimsPrincipal(new ClaimsIdentity()), true).Scope);
        Assert.Equal(TenantScope.None, TenantResolver.Resolve(null, true).Scope);
    }

    [Fact]
    public void StaffSchemeIdentity_IsStaff()
    {
        var user = Principal(Identity(AuthSchemes.StaffIdentity, new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())));

        Assert.Equal(new TenantIdentity(TenantScope.Staff, null), TenantResolver.Resolve(user, true));
    }

    [Fact]
    public void CustomerSchemeIdentity_IsThatCustomer()
    {
        var user = Principal(Identity(AuthSchemes.Customer, new Claim(JwtRegisteredClaimNames.Sub, CustomerId.ToString())));

        Assert.Equal(new TenantIdentity(TenantScope.Customer, CustomerId), TenantResolver.Resolve(user, true));
    }

    [Fact]
    public void CustomerIdentityWithoutUsableSub_IsNone()
    {
        Assert.Equal(TenantScope.None, TenantResolver.Resolve(Principal(Identity(AuthSchemes.Customer)), true).Scope);
        Assert.Equal(
            TenantScope.None,
            TenantResolver.Resolve(Principal(Identity(AuthSchemes.Customer, new Claim(JwtRegisteredClaimNames.Sub, "x"))), true).Scope);
    }

    [Fact]
    public void ClaimsAloneDoNotMakeAStaffMember()
    {
        // A token validated by Google (or anything but the staff scheme) carrying staff-looking
        // claims must not see company data — only the validating scheme counts.
        var user = Principal(Identity(
            AuthSchemes.Google,
            new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new Claim("pv", "1"),
            new Claim(ClaimTypes.Role, "owner")));

        Assert.Equal(TenantScope.None, TenantResolver.Resolve(user, true).Scope);
    }

    [Fact]
    public void StaffAndCustomerAtOnce_FailsClosed()
    {
        var user = Principal(
            Identity(AuthSchemes.StaffIdentity, new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())),
            Identity(AuthSchemes.Customer, new Claim(JwtRegisteredClaimNames.Sub, CustomerId.ToString())));

        Assert.Equal(TenantScope.None, TenantResolver.Resolve(user, true).Scope);
    }
}
