using FluentValidation;
using Office.Api.Data.Entities;

namespace Office.Api.Features.Subscriptions;

public class CreateSubscriptionRequestValidator : AbstractValidator<CreateSubscriptionRequest>
{
    public CreateSubscriptionRequestValidator()
    {
        RuleFor(x => x.Tier)
            .Must(t => Enum.TryParse<CustomerPlanTier>(t, ignoreCase: true, out _))
            .WithMessage("Тарифи нодуруст.");
        RuleFor(x => x.Months).GreaterThan(0).WithMessage("Муддати обуна бояд аз 0 зиёд бошад.");
    }
}

public class RejectSubscriptionRequestValidator : AbstractValidator<RejectSubscriptionRequest>
{
    public RejectSubscriptionRequestValidator()
    {
        // Required: the customer only ever learns why through this note.
        RuleFor(x => x.Note)
            .NotEmpty().WithMessage("Сабаби радкуниро нависед.")
            .MaximumLength(1000).WithMessage("Сабаб набояд аз 1000 аломат зиёд бошад.");
    }
}
