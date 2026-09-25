using Office.Api.Features.CustomerChats;

namespace Office.Api.Tests.Features.CustomerChats;

public class SendCustomerMessageRequestValidatorTests
{
    private readonly SendCustomerMessageRequestValidator _validator = new();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t")]
    public void Blank_IsRefused(string body) => Assert.False(_validator.Validate(new SendCustomerMessageRequest(body)).IsValid);

    [Fact]
    public void UpToInstagramsLimit_IsAccepted()
    {
        Assert.True(_validator.Validate(new SendCustomerMessageRequest("Салом!")).IsValid);
        Assert.True(_validator.Validate(new SendCustomerMessageRequest(new string('a', 1000))).IsValid);
        Assert.False(_validator.Validate(new SendCustomerMessageRequest(new string('a', 1001))).IsValid);
    }
}
