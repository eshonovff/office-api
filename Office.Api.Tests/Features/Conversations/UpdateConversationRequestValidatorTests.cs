using Office.Api.Features.Conversations;

namespace Office.Api.Tests.Features.Conversations;

public class UpdateConversationRequestValidatorTests
{
    private readonly UpdateConversationRequestValidator _validator = new();

    [Fact]
    public void Validate_OnlyStatus_IsValid()
    {
        var result = _validator.Validate(new UpdateConversationRequest("Closed", null));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_OnlyAssignedTo_IsValid()
    {
        var result = _validator.Validate(new UpdateConversationRequest(null, Guid.NewGuid()));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_NeitherFieldSet_IsInvalid()
    {
        var result = _validator.Validate(new UpdateConversationRequest(null, null));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_UnknownStatus_IsInvalid()
    {
        var result = _validator.Validate(new UpdateConversationRequest("NotAStatus", null));
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("new")]
    [InlineData("InProgress")]
    [InlineData("waiting")]
    [InlineData("CLOSED")]
    public void Validate_KnownStatus_CaseInsensitive_IsValid(string status)
    {
        var result = _validator.Validate(new UpdateConversationRequest(status, null));
        Assert.True(result.IsValid);
    }
}
