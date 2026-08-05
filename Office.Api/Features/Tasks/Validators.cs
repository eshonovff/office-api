using FluentValidation;
using Office.Api.Data.Entities;

namespace Office.Api.Features.Tasks;

public class CreateTaskRequestValidator : AbstractValidator<CreateTaskRequest>
{
    public CreateTaskRequestValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.ColumnId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Priority).Must(BeAValidPriority).WithMessage("Приоритети нодуруст.");
    }

    private static bool BeAValidPriority(string priority) => Enum.TryParse<TaskPriority>(priority, ignoreCase: true, out _);
}

public class UpdateTaskRequestValidator : AbstractValidator<UpdateTaskRequest>
{
    public UpdateTaskRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Priority).Must(BeAValidPriority).WithMessage("Приоритети нодуруст.");
    }

    private static bool BeAValidPriority(string priority) => Enum.TryParse<TaskPriority>(priority, ignoreCase: true, out _);
}

public class MoveTaskRequestValidator : AbstractValidator<MoveTaskRequest>
{
    public MoveTaskRequestValidator()
    {
        RuleFor(x => x.ColumnId).NotEmpty();
        RuleFor(x => x)
            .Must(x => x.BeforeTaskId != x.AfterTaskId || x.BeforeTaskId is null)
            .WithMessage("beforeTaskId ва afterTaskId набояд якхела бошанд.");
    }
}

public class CreateCommentRequestValidator : AbstractValidator<CreateCommentRequest>
{
    public CreateCommentRequestValidator()
    {
        RuleFor(x => x.Body).NotEmpty().MaximumLength(5000);
    }
}
