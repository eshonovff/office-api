using Office.Api.Common;

namespace Office.Api.Tests.Common;

public class PhoneNumberTests
{
    [Theory]
    [InlineData("+992927777777", "992927777777")]
    [InlineData("992927777777", "992927777777")]
    [InlineData("927777777", "992927777777")]
    [InlineData("+992 92 777 77 77", "992927777777")]
    [InlineData("992-92-777-77-77", "992927777777")]
    public void Normalize_ValidFormats_ReturnsCanonical(string input, string expected)
    {
        Assert.Equal(expected, PhoneNumber.Normalize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("12345")]
    [InlineData("+7927777777")]
    [InlineData("9929277777777")]
    public void Normalize_InvalidFormats_ReturnsNull(string? input)
    {
        Assert.Null(PhoneNumber.Normalize(input));
    }

    [Fact]
    public void ToLocalDigits_StripsCountryCode()
    {
        Assert.Equal("927777777", PhoneNumber.ToLocalDigits("992927777777"));
    }
}
