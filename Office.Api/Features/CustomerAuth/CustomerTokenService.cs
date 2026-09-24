using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Office.Api.Data.Entities;

namespace Office.Api.Features.CustomerAuth;

public record CustomerRefreshTokenPlain(string Value, string Hash);

public interface ICustomerTokenService
{
    string CreateAccessToken(Customer customer);
    CustomerRefreshTokenPlain CreateRefreshToken();
    string HashRefreshToken(string plainToken);
}

/// <summary>
/// Ҳамон алгуи TokenService (кормандон), вале бо калиди имзои ҷудогона (Jwt:CustomerKey) —
/// пас токени мизоҷ ҳатто аз рӯи имзо ба схемаи пешфарзи staff мувофиқ намеояд. Претензияҳо
/// қасдан бе роль/permission: мизоҷ ҳеҷ гоҳ "perm" claim надорад, пас RequirePermission ҳеҷ
/// гоҳ токени мизоҷро қабул карда наметавонад, ҳатто агар касе хато дар scheme кунад.
/// </summary>
public class CustomerTokenService(IConfiguration configuration) : ICustomerTokenService
{
    public string CreateAccessToken(Customer customer)
    {
        var key = configuration["Jwt:CustomerKey"]
            ?? throw new InvalidOperationException("Jwt:CustomerKey танзим нашудааст.");
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, customer.Id.ToString()),
            new(ClaimTypes.Email, customer.Email),
            new("type", "customer"),
        };

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public CustomerRefreshTokenPlain CreateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        var plainToken = Convert.ToBase64String(bytes);
        return new CustomerRefreshTokenPlain(plainToken, HashRefreshToken(plainToken));
    }

    public string HashRefreshToken(string plainToken)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plainToken)));
}
