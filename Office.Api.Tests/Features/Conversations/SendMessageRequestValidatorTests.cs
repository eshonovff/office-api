using Office.Api.Features.Conversations;

namespace Office.Api.Tests.Features.Conversations;

public class SendMessageRequestValidatorTests
{
    private readonly SendMessageRequestValidator _validator = new();

    [Fact]
    public void Validate_OnlyBody_IsValid()
    {
        var result = _validator.Validate(new SendMessageRequest("Салом!", null, null, null));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_OnlyTemplateName_IsValid()
    {
        var result = _validator.Validate(new SendMessageRequest(null, "order_ready", "en_US", null));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_NeitherBodyNorTemplate_IsInvalid()
    {
        var result = _validator.Validate(new SendMessageRequest(null, null, null, null));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_BlankBodyAndNoTemplate_IsInvalid()
    {
        var result = _validator.Validate(new SendMessageRequest("   ", null, null, null));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_BodyTooLong_IsInvalid()
    {
        var result = _validator.Validate(new SendMessageRequest(new string('a', 4097), null, null, null));
        Assert.False(result.IsValid);
    }
}
