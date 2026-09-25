using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Configuration;
using Office.Api.Data.Entities;
using Office.Api.Features.CustomerAuth;

namespace Office.Api.Tests.Features.CustomerAuth;

public class CustomerTokenServiceTests
{
    [Fact]
    public void AccessToken_CarriesTheSessionVersion_ThatAPasswordResetBumps()
    {
        var service = new CustomerTokenService(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:CustomerKey"] = new string('k', 64) })
            .Build());
        var customer = new Customer { Id = Guid.NewGuid(), Email = "a@example.com", FullName = "A", SessionVersion = 3 };

        var token = new JwtSecurityTokenHandler().ReadJwtToken(service.CreateAccessToken(customer));

        Assert.Equal("3", token.Claims.Single(c => c.Type == "sv").Value);
    }
}
