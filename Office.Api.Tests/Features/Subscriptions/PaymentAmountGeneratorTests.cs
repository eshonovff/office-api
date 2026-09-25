using Office.Api.Features.Subscriptions;

namespace Office.Api.Tests.Features.Subscriptions;

public class PaymentAmountGeneratorTests
{
    [Fact]
    public void Generate_AddsBetweenOneAndNinetyNineDirams()
    {
        var random = new Random(42);

        for (var i = 0; i < 200; i++)
        {
            var amount = PaymentAmountGenerator.Generate(200m, new HashSet<decimal>(), random);

            Assert.InRange(amount, 200.01m, 200.99m);
            Assert.Equal(amount, Math.Round(amount, 2));
        }
    }

    [Fact]
    public void Generate_OnlyOneFreeAmount_ReturnsThatOne()
    {
        var taken = Enumerable.Range(1, 99).Where(d => d != 37).Select(d => 200m + d / 100m).ToHashSet();

        var amount = PaymentAmountGenerator.Generate(200m, taken, new Random(1));

        Assert.Equal(200.37m, amount);
    }

    [Fact]
    public void Generate_NeverPicksATakenAmountWhileFreeOnesRemain()
    {
        var taken = Enumerable.Range(1, 50).Select(d => 600m + d / 100m).ToHashSet();
        var random = new Random(7);

        for (var i = 0; i < 200; i++)
        {
            var amount = PaymentAmountGenerator.Generate(600m, taken, random);
            Assert.DoesNotContain(amount, taken);
        }
    }

    [Fact]
    public void Generate_AllTaken_StillReturnsAValidAmount()
    {
        var taken = Enumerable.Range(1, 99).Select(d => 200m + d / 100m).ToHashSet();

        var amount = PaymentAmountGenerator.Generate(200m, taken, new Random(3));

        Assert.InRange(amount, 200.01m, 200.99m);
    }

    [Fact]
    public void Generate_TakenAmountsForADifferentBase_AreIgnored()
    {
        var taken = Enumerable.Range(1, 99).Where(d => d != 5).Select(d => 200m + d / 100m).ToHashSet();

        var amount = PaymentAmountGenerator.Generate(600m, taken, new Random(9));

        Assert.InRange(amount, 600.01m, 600.99m);
    }
}
