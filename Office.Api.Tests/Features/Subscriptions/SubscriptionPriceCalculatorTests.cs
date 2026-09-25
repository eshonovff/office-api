using Office.Api.Features.Subscriptions;

namespace Office.Api.Tests.Features.Subscriptions;

public class SubscriptionPriceCalculatorTests
{
    [Fact]
    public void Calculate_NoDiscount_IsPriceTimesMonths()
    {
        Assert.Equal(200m, SubscriptionPriceCalculator.Calculate(200m, 1, 0m));
    }

    [Theory]
    [InlineData(200, 3, 6, 564)]    // 600 − 6%
    [InlineData(200, 6, 15, 1020)]  // 1200 − 15%
    public void Calculate_AppliesTheDurationDiscount(decimal price, int months, decimal discount, decimal expected)
    {
        Assert.Equal(expected, SubscriptionPriceCalculator.Calculate(price, months, discount));
    }

    [Fact]
    public void Calculate_RoundsDownToWholeSomoni()
    {
        // 350 × 3 = 1050, −6% = 987.00 exactly; 333 × 3 = 999, −6% = 939.06 → 939.
        Assert.Equal(987m, SubscriptionPriceCalculator.Calculate(350m, 3, 6m));
        Assert.Equal(939m, SubscriptionPriceCalculator.Calculate(333m, 3, 6m));
    }
}
