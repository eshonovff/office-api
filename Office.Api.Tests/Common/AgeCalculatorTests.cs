using Office.Api.Common;

namespace Office.Api.Tests.Common;

public class AgeCalculatorTests
{
    [Fact]
    public void Calculate_BirthdayAlreadyPassedThisYear_ReturnsFullYears()
    {
        var birthDate = new DateOnly(1998, 5, 12);
        var asOf = new DateOnly(2026, 8, 6);

        Assert.Equal(28, AgeCalculator.Calculate(birthDate, asOf));
    }

    [Fact]
    public void Calculate_BirthdayNotYetReachedThisYear_DoesNotCountCurrentYear()
    {
        var birthDate = new DateOnly(1998, 12, 25);
        var asOf = new DateOnly(2026, 8, 6);

        Assert.Equal(27, AgeCalculator.Calculate(birthDate, asOf));
    }

    [Fact]
    public void Calculate_ExactlyOnBirthday_CountsCurrentYear()
    {
        var birthDate = new DateOnly(1998, 8, 6);
        var asOf = new DateOnly(2026, 8, 6);

        Assert.Equal(28, AgeCalculator.Calculate(birthDate, asOf));
    }
}
