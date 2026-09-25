namespace Office.Api.Features.CustomerAuth;

public enum ExternalLoginAction
{
    /// <summary>Ин sub аллакай ба як Customer пайваст аст — ҳамонро истифода баред.</summary>
    UseLinkedCustomer,

    /// <summary>Sub нав аст, вале email ба ҳисоби мавҷуда (email+parol ё провайдери дигар) тааллуқ дорад — пайваст кунед.</summary>
    LinkToExistingCustomerByEmail,

    /// <summary>Sub нав, email ҳам нав — ҳисоби нав созед (EmailVerifiedAt фавран, провайдер аллакай тасдиқ кардааст).</summary>
    CreateNewCustomer,

    /// <summary>
    /// Email ба ҳисоби мавҷуда тааллуқ дорад, вале провайдер онро тасдиқшуда надонист —
    /// пайваст накунед (вагарна касе бо email-и тасдиқнашуда метавонад ҳисоби каси дигарро гирад).
    /// </summary>
    RejectUnverifiedEmail,
}

/// <summary>
/// Қарори "ин sub-и Google/Apple ба кадом Customer мерасад" — pure, бе DB, то ҷудо тест
/// кард. CustomerAuthEndpoints ду AnyAsync (аз рӯи sub, баъд аз рӯи email)-ро иҷро мекунад
/// ва натиҷаро ба ин мегузаронад.
/// </summary>
public static class ExternalLoginResolver
{
    public static ExternalLoginAction Resolve(bool hasLinkedCustomer, bool hasCustomerWithSameEmail, bool providerEmailVerified)
    {
        if (hasLinkedCustomer)
            return ExternalLoginAction.UseLinkedCustomer;

        if (hasCustomerWithSameEmail)
            return providerEmailVerified ? ExternalLoginAction.LinkToExistingCustomerByEmail : ExternalLoginAction.RejectUnverifiedEmail;

        return ExternalLoginAction.CreateNewCustomer;
    }
}
