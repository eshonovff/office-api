using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Office.Api.Auth;

namespace Office.Api.Common;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? throw new InvalidOperationException("Токен claim-и sub надорад.");
        return Guid.Parse(sub);
    }

    /// <summary>Санҷиши иловагии permission дар дохили handler (мас. кадоме, ки танҳо ба як шохаи вазифа лозим аст).</summary>
    public static bool HasPermission(this ClaimsPrincipal principal, string permission) =>
        principal.IsInRole(RoleKeys.Owner) || principal.FindAll("perm").Any(c => c.Value == permission);
}
