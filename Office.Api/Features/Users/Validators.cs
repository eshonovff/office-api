using FluentValidation;
using Permissions = Office.Api.Auth.Permissions;

namespace Office.Api.Features.Users;

public class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Username).NotEmpty().MaximumLength(100).Matches("^[a-zA-Z0-9_.]+$")
            .WithMessage("Логин бояд танҳо аз ҳарф, рақам, `_` ва `.` иборат бошад.");
        RuleFor(x => x.Phone).MaximumLength(30);
    }
}

public class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Phone).MaximumLength(30);
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
