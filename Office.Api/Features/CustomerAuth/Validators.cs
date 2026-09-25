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

public class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
    }
}

public class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(128);
        // BCrypt only reads the first 72 bytes; 128 characters is a sane ceiling.
        RuleFor(x => x.NewPassword)
            .MinimumLength(8).WithMessage("Рамз бояд ҳадди ақал 8 аломат бошад.")
            .MaximumLength(128).WithMessage("Рамз набояд аз 128 аломат зиёд бошад.");
    }
}
