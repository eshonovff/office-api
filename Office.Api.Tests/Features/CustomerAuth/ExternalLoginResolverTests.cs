using Office.Api.Features.CustomerAuth;

namespace Office.Api.Tests.Features.CustomerAuth;

public class ExternalLoginResolverTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Resolve_LinkedCustomerExists_ReturnsUseLinkedCustomer_RegardlessOfEmailState(bool hasCustomerWithSameEmail)
    {
        var action = ExternalLoginResolver.Resolve(
            hasLinkedCustomer: true, hasCustomerWithSameEmail, providerEmailVerified: false);

        Assert.Equal(ExternalLoginAction.UseLinkedCustomer, action);
    }

    [Fact]
    public void Resolve_NoLinkAndNoEmailMatch_ReturnsCreateNewCustomer()
    {
        var action = ExternalLoginResolver.Resolve(
            hasLinkedCustomer: false, hasCustomerWithSameEmail: false, providerEmailVerified: false);

        Assert.Equal(ExternalLoginAction.CreateNewCustomer, action);
    }

    [Fact]
    public void Resolve_EmailMatchesExistingCustomer_ProviderVerifiedEmail_ReturnsLinkToExistingCustomer()
    {
        var action = ExternalLoginResolver.Resolve(
            hasLinkedCustomer: false, hasCustomerWithSameEmail: true, providerEmailVerified: true);

        Assert.Equal(ExternalLoginAction.LinkToExistingCustomerByEmail, action);
    }

    [Fact]
    public void Resolve_EmailMatchesExistingCustomer_ProviderDidNotVerifyEmail_ReturnsRejectUnverifiedEmail()
    {
        // Ҳимоя аз account takeover: касе бо email-и тасдиқнашудаи провайдер набояд ҳисоби
        // дигареро (сохташуда бо ҳамон email тавассути email+parol) гирад.
        var action = ExternalLoginResolver.Resolve(
            hasLinkedCustomer: false, hasCustomerWithSameEmail: true, providerEmailVerified: false);

        Assert.Equal(ExternalLoginAction.RejectUnverifiedEmail, action);
    }
}
