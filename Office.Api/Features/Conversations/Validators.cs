using FluentValidation;
using Office.Api.Data.Entities;

namespace Office.Api.Features.Conversations;

public class SendMessageRequestValidator : AbstractValidator<SendMessageRequest>
{
    public SendMessageRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Body) || !string.IsNullOrWhiteSpace(x.TemplateName))
            .WithMessage("Ё матн (body), ё номи шаблон (templateName) лозим аст.");

        // Ёддошти дохилӣ ба мижоз намерасад — шаблон барои он маъно надорад.
        RuleFor(x => x)
            .Must(x => !x.IsInternalNote || string.IsNullOrEmpty(x.TemplateName))
            .WithMessage("Ёддошти дохилӣ (isInternalNote) бо шаблон якҷоя буда наметавонад.");

        RuleFor(x => x.Body).MaximumLength(4096);
        RuleFor(x => x.TemplateName).MaximumLength(200);
        RuleFor(x => x.TemplateLanguage).MaximumLength(20);
    }
}

public class UpdateConversationRequestValidator : AbstractValidator<UpdateConversationRequest>
{
    public UpdateConversationRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => !string.IsNullOrEmpty(x.Status) || x.AssignedTo is not null)
            .WithMessage("Ё статус, ё корманди таъиншуда лозим аст.");

        RuleFor(x => x.Status)
            .Must(status => Enum.TryParse<ConversationStatus>(status, ignoreCase: true, out _))
            .When(x => !string.IsNullOrEmpty(x.Status))
            .WithMessage("Статус нодуруст аст.");
    }
}
