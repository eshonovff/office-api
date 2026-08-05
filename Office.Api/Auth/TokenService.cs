using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Office.Api.Data.Entities;

namespace Office.Api.Auth;

public record RefreshTokenPlain(string Value, string Hash);

public interface ITokenService
{
    string CreateAccessToken(User user, PermissionResolution permissions);
    RefreshTokenPlain CreateRefreshToken();
    string HashRefreshToken(string plainToken);
}

public class TokenService(IConfiguration configuration) : ITokenService
{
    public string CreateAccessToken(User user, PermissionResolution permissions)
    {
        var key = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key танзим нашудааст.");
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new("pv", permissions.PermissionsVersion.ToString(CultureInfo.InvariantCulture)),
        };

        foreach (var role in permissions.Roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        foreach (var permission in permissions.Permissions)
            claims.Add(new Claim("perm", permission));

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public RefreshTokenPlain CreateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        var plainToken = Convert.ToBase64String(bytes);
        return new RefreshTokenPlain(plainToken, HashRefreshToken(plainToken));
    }

    public string HashRefreshToken(string plainToken)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plainToken)));
}
