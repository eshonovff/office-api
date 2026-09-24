using Office.Api.Data.Entities;
using Office.Api.Features.Subscriptions;

namespace Office.Api.Features.CustomerAuth;

public record RegisterCustomerRequest(string Email, string Password, string FullName);

public record VerifyCustomerEmailRequest(string Email, string Code);

public record ResendCustomerVerificationCodeRequest(string Email);

public record CustomerLoginRequest(string Email, string Password);

/// <summary>Status: Trial | Active | Expired. Tier: Pro | Creator | Premium (null during a plain trial).</summary>
public record CustomerAccessDto(string Status, string? Tier, DateTimeOffset? EndsAt, bool HasAccess);

public record CustomerMeResponse(
    Guid Id, string Email, string FullName, string? AvatarUrl, bool EmailVerified, CustomerAccessDto Access)
{
    public static CustomerMeResponse From(Customer customer)
    {
        var access = CustomerAccessResolver.Resolve(
            DateTimeOffset.UtcNow, customer.TrialEndsAt, customer.PlanTier, customer.PlanExpiresAt);

        return new CustomerMeResponse(
            customer.Id,
            customer.Email,
            customer.FullName,
            customer.AvatarUrl,
            customer.EmailVerifiedAt is not null,
            new CustomerAccessDto(access.Status.ToString(), access.Tier?.ToString(), access.EndsAt, access.HasAccess));
    }
}

/// <summary>Ҷавоби register/resend — токен намедиҳад, чун email ҳанӯз тасдиқ нашудааст.</summary>
public record CustomerAuthMessageResponse(string Message);

public record CustomerAuthResponse(string AccessToken, CustomerMeResponse Customer);
