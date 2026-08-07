using FluentValidation;
using Office.Api.Common;
using Office.Api.Data.Entities;
using Permissions = Office.Api.Auth.Permissions;

namespace Office.Api.Features.Users;

public class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Phone).NotEmpty()
            .Must(phone => PhoneNumber.Normalize(phone) is not null)
            .WithMessage("Рақами телефон нодуруст аст. Формат: +992XXXXXXXXX.");
        RuleFor(x => x.Email).MaximumLength(200).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email));
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.Gender)
            .Must(gender => Enum.TryParse<Gender>(gender, ignoreCase: true, out _))
            .When(x => !string.IsNullOrEmpty(x.Gender))
            .WithMessage("Ҷинсият бояд `Male` ё `Female` бошад.");
    }
}

public class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).MaximumLength(200).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email));
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.Gender)
            .Must(gender => Enum.TryParse<Gender>(gender, ignoreCase: true, out _))
            .When(x => !string.IsNullOrEmpty(x.Gender))
            .WithMessage("Ҷинсият бояд `Male` ё `Female` бошад.");
    }
}

public class SetUserRolesRequestValidator : AbstractValidator<SetUserRolesRequest>
{
    public SetUserRolesRequestValidator()
    {
        RuleFor(x => x.RoleIds).NotNull();
    }
}

public class SetUserPermissionsRequestValidator : AbstractValidator<SetUserPermissionsRequest>
{
    public SetUserPermissionsRequestValidator()
    {
        RuleFor(x => x.Exceptions).NotNull();
        RuleForEach(x => x.Exceptions)
            .ChildRules(exception =>
            {
                exception.RuleFor(e => e.PermissionKey)
                    .Must(key => Permissions.All.Contains(key))
                    .WithMessage("Калиди permission нодуруст аст.");
            });
    }
}
