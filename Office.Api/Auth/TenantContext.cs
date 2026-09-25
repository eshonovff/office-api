using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Office.Api.Auth;

/// <summary>Whose channel data a DbContext may see — enforced by AppDbContext's query filters.</summary>
public enum TenantScope
{
    /// <summary>An HTTP request with no recognised caller: sees no channel data at all.</summary>
    None,

    /// <summary>Staff (default JWT scheme): company channels only (<c>Channel.CustomerId == null</c>).</summary>
    Staff,

    /// <summary>A мизоҷ (Customer JWT scheme): only the channels they own.</summary>
    Customer,

    /// <summary>
    /// No HTTP request at all — Hangfire jobs (WebhookProcessor, token refresh, …) and startup:
    /// everything, because routing a webhook to its channel must work for every owner.
    /// </summary>
    System,
}

public readonly record struct TenantIdentity(TenantScope Scope, Guid? CustomerId);

public interface ITenantContext
{
    TenantIdentity Current { get; }
}

public static class TenantResolver
{
    private static readonly TenantIdentity Nobody = new(TenantScope.None, null);

    /// <summary>
    /// Decides by which JWT scheme validated the identity (its AuthenticationType, set per
    /// scheme in Program.cs), not by claims — a claim can be copied into another token, the
    /// signing key behind a scheme cannot. Anything unrecognised fails closed to None.
    /// </summary>
    public static TenantIdentity Resolve(ClaimsPrincipal? user, bool inHttpRequest)
    {
        if (!inHttpRequest)
            return new TenantIdentity(TenantScope.System, null);

        var identities = user?.Identities.Where(i => i.IsAuthenticated).ToList() ?? [];
        var staff = identities.Any(i => i.AuthenticationType == AuthSchemes.StaffIdentity);
        var customer = identities.FirstOrDefault(i => i.AuthenticationType == AuthSchemes.Customer);

        // Different signing keys make "both at once" impossible; if it ever happens, nobody can
        // say whose data this request is for — so it gets none.
        if (staff && customer is not null)
            return Nobody;

        if (staff)
            return new TenantIdentity(TenantScope.Staff, null);

        if (customer is not null &&
            Guid.TryParse(customer.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var customerId))
        {
            return new TenantIdentity(TenantScope.Customer, customerId);
        }

        return Nobody;
    }
}

public sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    // Resolved on every read, never cached: on мизоҷ endpoints the authorization middleware
    // replaces HttpContext.User with the Customer-scheme principal after the request began.
    public TenantIdentity Current =>
        TenantResolver.Resolve(accessor.HttpContext?.User, accessor.HttpContext is not null);
}
