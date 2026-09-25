using Office.Api.Features.CustomerAuth;

namespace Office.Api.Tests.Features.CustomerAuth;

public class PasswordResetValidatorsTests
{
    [Theory]
    [InlineData("1234567", false)]
    [InlineData("12345678", true)]
    public void NewPassword_AtLeastEight(string password, bool valid)
    {
        var result = new ResetPasswordRequestValidator().Validate(new ResetPasswordRequest("token", password));
        Assert.Equal(valid, result.IsValid);
    }

    [Fact]
    public void NewPassword_AtMost128()
    {
        var validator = new ResetPasswordRequestValidator();
        Assert.True(validator.Validate(new ResetPasswordRequest("token", new string('a', 128))).IsValid);
        Assert.False(validator.Validate(new ResetPasswordRequest("token", new string('a', 129))).IsValid);
    }

    [Fact]
    public void Token_RequiredAndBounded()
    {
        var validator = new ResetPasswordRequestValidator();
        Assert.False(validator.Validate(new ResetPasswordRequest("", "12345678")).IsValid);
        Assert.False(validator.Validate(new ResetPasswordRequest(new string('x', 129), "12345678")).IsValid);
    }

    [Theory]
    [InlineData("a@example.com", true)]
    [InlineData("not-an-email", false)]
    [InlineData("", false)]
    public void ForgotPassword_NeedsAnEmail(string email, bool valid)
    {
        Assert.Equal(valid, new ForgotPasswordRequestValidator().Validate(new ForgotPasswordRequest(email)).IsValid);
    }
}
