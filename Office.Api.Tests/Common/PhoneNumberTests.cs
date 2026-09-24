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

    // A staff username IS the normalized phone (UsersEndpoints.CreateAsync). The shared sign-in
    // page (office-web /login) sends anything with an "@" to the мизоҷ login instead — so a
    // staff username must never be able to contain one.
    [Theory]
    [InlineData("992927777777@example.com")]
    [InlineData("+992 92 777 77 77")]
    [InlineData("927777777")]
    public void Normalize_NeverYieldsAnythingButDigits(string input)
    {
        var normalized = PhoneNumber.Normalize(input);
        Assert.True(normalized is null || normalized.All(char.IsAsciiDigit), normalized);
    }

    [Fact]
    public void ToLocalDigits_StripsCountryCode()
    {
        Assert.Equal("927777777", PhoneNumber.ToLocalDigits("992927777777"));
    }
}
