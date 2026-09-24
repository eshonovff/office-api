namespace Office.Api.Data.Entities;

/// <summary>
/// Пайвасти як ҳисоби Customer ба як "sub" (provider user id) — то як мизоз бо Google ВА
/// Apple ВА email+parol якҷоя ба ҳамон як ҳисоб ворид шавад (пайваст аз рӯи email-и
/// тасдиқшудаи провайдер, ниг. ExternalLoginResolver).
/// </summary>
public class CustomerExternalLogin
{
    public Guid Id { get; set; }

    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public CustomerExternalLoginProvider Provider { get; set; }

    /// <summary>Claim-и "sub"-и провайдер — опак, GUID нест (масалан Google-ро рақамӣ мефиристад).</summary>
    public required string ProviderUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
