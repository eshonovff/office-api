using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Office.Api.Common;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? throw new InvalidOperationException("Токен claim-и sub надорад.");
        return Guid.Parse(sub);
    }
}
