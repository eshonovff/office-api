using FluentValidation;
using Permissions = Office.Api.Auth.Permissions;

namespace Office.Api.Features.Roles;

public class CreateRoleRequestValidator : AbstractValidator<CreateRoleRequest>
{
    public CreateRoleRequestValidator()
    {
        RuleFor(x => x.Key).NotEmpty().MaximumLength(50).Matches("^[a-z0-9_.]+$")
            .WithMessage("Калиди роль бояд танҳо аз ҳарфи хурд, рақам, `_` ва `.` иборат бошад.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public class UpdateRoleRequestValidator : AbstractValidator<UpdateRoleRequest>
{
    public UpdateRoleRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public class SetRolePermissionsRequestValidator : AbstractValidator<SetRolePermissionsRequest>
{
    public SetRolePermissionsRequestValidator()
    {
        RuleFor(x => x.PermissionKeys).NotNull();
        RuleForEach(x => x.PermissionKeys)
            .Must(key => Permissions.All.Contains(key))
            .WithMessage("Калиди permission нодуруст аст.");
    }
}
