using Office.Api.Common;

namespace Office.Api.Tests.Common;

public class PasswordGeneratorTests
{
    [Fact]
    public void GenerateNumeric_DefaultLength_ReturnsEightDigits()
    {
        var password = PasswordGenerator.GenerateNumeric();

        Assert.Equal(8, password.Length);
        Assert.All(password, c => Assert.True(char.IsDigit(c)));
    }

    [Fact]
    public void GenerateNumeric_CalledTwice_ProducesDifferentValues()
    {
        var first = PasswordGenerator.GenerateNumeric();
        var second = PasswordGenerator.GenerateNumeric();

        Assert.NotEqual(first, second);
    }
}
