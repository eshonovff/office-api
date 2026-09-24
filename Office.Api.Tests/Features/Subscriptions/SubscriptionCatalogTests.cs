using Office.Api.Features.Subscriptions;

namespace Office.Api.Tests.Features.Subscriptions;

public class SubscriptionCatalogTests
{
    private static readonly SubscriptionCatalog Catalog = new(
        TrialDays: 7,
        PaymentWindow: TimeSpan.FromMinutes(5),
        Currency: "TJS",
        DurationMonths: [1],
        Plans: [],
        PaymentCards:
        [
            new PaymentCardOptions { Bank = "Dushanbe City", BankCode = "dc", CardNumber = "5058270379023210" },
            new PaymentCardOptions { Bank = "Alif", BankCode = "alif", CardNumber = "5058 2702 8510 4567" },
        ]);

    [Fact]
    public void FindPaymentCard_ExactNumber_ReturnsThatCard()
    {
        Assert.Equal("Dushanbe City", Catalog.FindPaymentCard("5058270379023210")?.Bank);
    }

    [Fact]
    public void FindPaymentCard_IgnoresSpacingOnEitherSide()
    {
        // Config has spaces, client sends none — and the other way round.
        Assert.Equal("Alif", Catalog.FindPaymentCard("5058270285104567")?.Bank);
        Assert.Equal("Dushanbe City", Catalog.FindPaymentCard("5058 2703 7902 3210")?.Bank);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1111222233334444")]
    public void FindPaymentCard_MissingOrUnknown_ReturnsNull(string? cardNumber)
    {
        Assert.Null(Catalog.FindPaymentCard(cardNumber));
    }
}
