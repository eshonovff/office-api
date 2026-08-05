using FluentValidation;

namespace Office.Api.Features.Projects;

public class CreateProjectRequestValidator : AbstractValidator<CreateProjectRequest>
{
    public CreateProjectRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Key).NotEmpty().MaximumLength(20).Matches("^[A-Z0-9_-]+$")
            .WithMessage("Калиди проект бояд танҳо аз ҳарфи калон, рақам, `_` ва `-` иборат бошад.");
        RuleFor(x => x.Color).MaximumLength(20);
    }
}

public class UpdateProjectRequestValidator : AbstractValidator<UpdateProjectRequest>
{
    public UpdateProjectRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Color).MaximumLength(20);
    }
}

public class SetProjectMembersRequestValidator : AbstractValidator<SetProjectMembersRequest>
{
    public SetProjectMembersRequestValidator()
    {
        RuleFor(x => x.UserIds).NotNull();
    }
}

public class CreateColumnRequestValidator : AbstractValidator<CreateColumnRequest>
{
    public CreateColumnRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}

public class UpdateColumnRequestValidator : AbstractValidator<UpdateColumnRequest>
{
    public UpdateColumnRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}

public class ReorderColumnsRequestValidator : AbstractValidator<ReorderColumnsRequest>
{
    public ReorderColumnsRequestValidator()
    {
        RuleFor(x => x.ColumnIds).NotNull().NotEmpty();
    }
}

public class CreateLabelRequestValidator : AbstractValidator<CreateLabelRequest>
{
    public CreateLabelRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Color).MaximumLength(20);
    }
}

public class UpdateLabelRequestValidator : AbstractValidator<UpdateLabelRequest>
{
    public UpdateLabelRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Color).MaximumLength(20);
    }
}
