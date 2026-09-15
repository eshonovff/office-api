using FluentValidation;
using Office.Api.Channels.Automation;

namespace Office.Api.Features.CommentAutomation;

public class AutomationTriggerConfigValidator : AbstractValidator<AutomationTriggerConfig>
{
    public AutomationTriggerConfigValidator()
    {
        RuleFor(x => x.MatchMode).Must(m => m is AutomationTriggerConfig.MatchModeKeyword or AutomationTriggerConfig.MatchModeAll)
            .WithMessage("matchMode бояд 'keyword' ё 'all' бошад.");
        RuleFor(x => x.PostScope).Must(s => s is AutomationTriggerConfig.PostScopeAll or AutomationTriggerConfig.PostScopeSelected)
            .WithMessage("postScope бояд 'all' ё 'selected' бошад.");
        RuleFor(x => x.Keywords).NotEmpty().When(x => x.MatchMode == AutomationTriggerConfig.MatchModeKeyword)
            .WithMessage("Ҳадди ақал як калимаи калидӣ лозим аст.");
        RuleFor(x => x.PostIds).NotEmpty().When(x => x.PostScope == AutomationTriggerConfig.PostScopeSelected)
            .WithMessage("Ҳадди ақал як пост бояд интихоб шавад.");
    }
}

public class AutomationReplyActionValidator : AbstractValidator<AutomationReplyAction>
{
    public AutomationReplyActionValidator()
    {
        RuleFor(x => x.CommentReplies).NotEmpty().WithMessage("Ҳадди ақал як матни ҷавоб лозим аст.");
        RuleForEach(x => x.CommentReplies).NotEmpty();
        // DmText қасдан ихтиёрӣ аст — агар холӣ бошад, CommentAutomationJob DM-ро тамоман
        // намефиристад (DmStatus=Disabled). Корбар метавонад танҳо ба коментарий ҷавоб диҳад.
        RuleFor(x => x.DmButtonTitle).NotEmpty().MaximumLength(20)
            .When(x => !string.IsNullOrEmpty(x.DmButtonUrl))
            .WithMessage("Агар пайванди тугма дода шавад, сарлавҳаи тугма ҳам лозим аст (то 20 ҳарф).");
    }
}

public class CreateAutomationRuleRequestValidator : AbstractValidator<CreateAutomationRuleRequest>
{
    public CreateAutomationRuleRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CooldownMinutes).GreaterThanOrEqualTo(0);
        RuleFor(x => x.TriggerConfig).NotNull().SetValidator(new AutomationTriggerConfigValidator());
        RuleFor(x => x.ConditionConfig).NotNull();
        RuleFor(x => x.ActionConfig).NotNull().DependentRules(() =>
        {
            // DependentRules: ин лямбдаҳо ТАНҲО вақте иҷро мешаванд, ки NotNull() боло гузашт —
            // бе он x.ActionConfig.OnMatch метавонист NullReferenceException партояд (500, на 400).
            RuleFor(x => x.ActionConfig.OnMatch).NotNull().SetValidator(new AutomationReplyActionValidator());
            RuleFor(x => x.ActionConfig.OnNotFollowing).NotNull().SetValidator(new AutomationReplyActionValidator()!)
                .When(x => x.ConditionConfig is not null && x.ConditionConfig.RequiresFollow)
                .WithMessage("Агар 'Танҳо барои обунашудагон' фаъол бошад, ҷавоб барои обунанашудагон ҳам лозим аст.");
        });
    }
}

public class UpdateAutomationRuleRequestValidator : AbstractValidator<UpdateAutomationRuleRequest>
{
    public UpdateAutomationRuleRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CooldownMinutes).GreaterThanOrEqualTo(0);
        RuleFor(x => x.TriggerConfig).NotNull().SetValidator(new AutomationTriggerConfigValidator());
        RuleFor(x => x.ConditionConfig).NotNull();
        RuleFor(x => x.ActionConfig).NotNull().DependentRules(() =>
        {
            RuleFor(x => x.ActionConfig.OnMatch).NotNull().SetValidator(new AutomationReplyActionValidator());
            RuleFor(x => x.ActionConfig.OnNotFollowing).NotNull().SetValidator(new AutomationReplyActionValidator()!)
                .When(x => x.ConditionConfig is not null && x.ConditionConfig.RequiresFollow)
                .WithMessage("Агар 'Танҳо барои обунашудагон' фаъол бошад, ҷавоб барои обунанашудагон ҳам лозим аст.");
        });
    }
}

public class DryRunAutomationRuleRequestValidator : AbstractValidator<DryRunAutomationRuleRequest>
{
    public DryRunAutomationRuleRequestValidator()
    {
        RuleFor(x => x.TriggerConfig).NotNull().SetValidator(new AutomationTriggerConfigValidator());
        RuleFor(x => x.CommentText).NotNull();
    }
}
