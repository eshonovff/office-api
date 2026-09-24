using Office.Api.Data.Entities;

namespace Office.Api.Features.CustomerAuth;

public record RegisterCustomerRequest(string Email, string Password, string FullName);

public record VerifyCustomerEmailRequest(string Email, string Code);

public record ResendCustomerVerificationCodeRequest(string Email);

public record CustomerLoginRequest(string Email, string Password);

public record CustomerMeResponse(Guid Id, string Email, string FullName, string? AvatarUrl, bool EmailVerified)
{
    public static CustomerMeResponse From(Customer customer) => new(
        customer.Id, customer.Email, customer.FullName, customer.AvatarUrl, customer.EmailVerifiedAt is not null);
}

/// <summary>Ҷавоби register/resend — токен намедиҳад, чун email ҳанӯз тасдиқ нашудааст.</summary>
public record CustomerAuthMessageResponse(string Message);

public record CustomerAuthResponse(string AccessToken, CustomerMeResponse Customer);
