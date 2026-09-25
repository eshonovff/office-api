using FluentValidation;
using Office.Api.Channels.Automation;

namespace Office.Api.Features.CommentAutomation;

/// <summary>
/// Bounds on everything a rule stores. Rules are no longer staff-only — мизоҷон write them too
/// (CustomerCommentRulesEndpoints) — so each list and text has a ceiling: Instagram's own limits
/// where there is one (a message: 1000 characters, a button title: 20), a generous one elsewhere.
/// </summary>
public static class AutomationRuleLimits
{
    public const int MaxKeywords = 50;
    public const int MaxKeywordLength = 100;
    public const int MaxPostIds = 100;
    /// <summary>Instagram media ids: digits, some with an underscore.</summary>
    public const string PostIdPattern = "^[0-9_]{1,64}$";
    public const int MaxCommentReplies = 10;
    public const int MaxTextLength = 1000;
    public const int MaxButtonUrlLength = 500;
    public const int MaxCooldownMinutes = 30 * 24 * 60;
    /// <summary>An Instagram-scoped user id — only digits, since it goes into a Graph API path.</summary>
    public const string ActorIdPattern = "^[0-9]{1,32}$";

    public static bool IsHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}

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
        RuleFor(x => x.Keywords).Must(k => k.Length <= AutomationRuleLimits.MaxKeywords).When(x => x.Keywords is not null)
            .WithMessage($"То {AutomationRuleLimits.MaxKeywords} калимаи калидӣ.");
        RuleForEach(x => x.Keywords).MaximumLength(AutomationRuleLimits.MaxKeywordLength).When(x => x.Keywords is not null);
        RuleFor(x => x.PostIds).Must(p => p.Length <= AutomationRuleLimits.MaxPostIds).When(x => x.PostIds is not null)
            .WithMessage($"То {AutomationRuleLimits.MaxPostIds} пост.");
        RuleForEach(x => x.PostIds).Matches(AutomationRuleLimits.PostIdPattern).When(x => x.PostIds is not null)
            .WithMessage("Ин пост нест.");
    }
}

public class AutomationReplyActionValidator : AbstractValidator<AutomationReplyAction>
{
    public AutomationReplyActionValidator()
    {
        RuleFor(x => x.CommentReplies).NotEmpty().WithMessage("Ҳадди ақал як матни ҷавоб лозим аст.");
        RuleFor(x => x.CommentReplies).Must(r => r.Length <= AutomationRuleLimits.MaxCommentReplies).When(x => x.CommentReplies is not null)
            .WithMessage($"То {AutomationRuleLimits.MaxCommentReplies} матни ҷавоб.");
        RuleForEach(x => x.CommentReplies).NotEmpty().MaximumLength(AutomationRuleLimits.MaxTextLength);
        RuleFor(x => x.DmText).MaximumLength(AutomationRuleLimits.MaxTextLength);
        RuleFor(x => x.DmButtonUrl).MaximumLength(AutomationRuleLimits.MaxButtonUrlLength)
            .Must(AutomationRuleLimits.IsHttpUrl).When(x => !string.IsNullOrEmpty(x.DmButtonUrl))
            .WithMessage("Пайванди тугма бояд бо https:// сар шавад.");
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
        RuleFor(x => x.CooldownMinutes).InclusiveBetween(0, AutomationRuleLimits.MaxCooldownMinutes);
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
        RuleFor(x => x.CooldownMinutes).InclusiveBetween(0, AutomationRuleLimits.MaxCooldownMinutes);
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
        RuleFor(x => x.CommentText).NotNull().MaximumLength(2200); // an Instagram comment's own limit
        RuleFor(x => x.MediaId).Matches(AutomationRuleLimits.PostIdPattern).When(x => !string.IsNullOrEmpty(x.MediaId));
        RuleFor(x => x.ActorExternalId).Matches(AutomationRuleLimits.ActorIdPattern).When(x => !string.IsNullOrEmpty(x.ActorExternalId))
            .WithMessage("ID-и корбар танҳо рақам аст.");
    }
}
