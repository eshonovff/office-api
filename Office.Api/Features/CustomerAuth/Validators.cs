using FluentValidation;

namespace Office.Api.Features.CustomerAuth;

public class RegisterCustomerRequestValidator : AbstractValidator<RegisterCustomerRequest>
{
    public RegisterCustomerRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.Password).MinimumLength(8).WithMessage("Парол бояд ҳадди ақал 8 аломат бошад.");
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
    }
}

public class VerifyCustomerEmailRequestValidator : AbstractValidator<VerifyCustomerEmailRequest>
{
    public VerifyCustomerEmailRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Code).NotEmpty().Length(6).WithMessage("Код бояд 6 рақам бошад.");
    }
}

public class ResendCustomerVerificationCodeRequestValidator : AbstractValidator<ResendCustomerVerificationCodeRequest>
{
    public ResendCustomerVerificationCodeRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
    }
}

public class CustomerLoginRequestValidator : AbstractValidator<CustomerLoginRequest>
{
    public CustomerLoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}
