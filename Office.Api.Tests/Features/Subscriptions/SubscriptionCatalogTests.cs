using Microsoft.Extensions.Configuration;
using Office.Api.Features.Subscriptions;

namespace Office.Api.Tests.Features.Subscriptions;

public class SubscriptionCatalogTests
{
    private static readonly SubscriptionCatalog Catalog = new(
        TrialDays: 7,
        PaymentWindow: TimeSpan.FromMinutes(5),
        Currency: "TJS",
        Durations: [new SubscriptionDurationOptions { Months = 1 }],
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

    [Fact]
    public void Load_PlanLimits_AbsentCountMeansUnlimited()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Subscriptions:Plans:0:Tier"] = "Pro",
                ["Subscriptions:Plans:0:MonthlyPrice"] = "200",
                ["Subscriptions:Plans:0:Limits:Accounts"] = "1",
                ["Subscriptions:Plans:0:Limits:ActiveAutomations"] = "10",
                ["Subscriptions:Plans:1:Tier"] = "Premium",
                ["Subscriptions:Plans:1:MonthlyPrice"] = "1200",
                ["Subscriptions:Plans:1:Limits:WhatsAppBroadcasts"] = "true",
            })
            .Build();

        var plans = SubscriptionCatalog.Load(configuration).Plans;

        Assert.Equal(10, plans[0].Limits.ActiveAutomations);
        Assert.Equal(1, plans[0].Limits.Accounts);
        Assert.False(plans[0].Limits.WhatsAppBroadcasts);
        Assert.Null(plans[1].Limits.Accounts);
        Assert.Null(plans[1].Limits.ActiveAutomations);
        Assert.True(plans[1].Limits.WhatsAppBroadcasts);
    }

    [Fact]
    public void Load_PlanWithoutLimitsSection_IsUnlimited()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Subscriptions:Plans:0:Tier"] = "Creator",
                ["Subscriptions:Plans:0:MonthlyPrice"] = "450",
            })
            .Build();

        var limits = SubscriptionCatalog.Load(configuration).Plans[0].Limits;

        Assert.Null(limits.Accounts);
        Assert.Null(limits.ActiveAutomations);
        Assert.Null(limits.TeamMembers);
    }
}
