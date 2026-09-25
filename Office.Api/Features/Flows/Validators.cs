using FluentValidation;
using Office.Api.Channels.Automation;
using Office.Api.Data.Entities;

namespace Office.Api.Features.Flows;

public class CreateFlowRequestValidator : AbstractValidator<CreateFlowRequest>
{
    public CreateFlowRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TriggerType).Must(t => t is "instagram_comment" or "instagram_dm")
            .WithMessage("triggerType бояд 'instagram_comment' ё 'instagram_dm' бошад.");
        RuleFor(x => x.TriggerConfig).NotNull();
        RuleFor(x => x).Custom((request, context) =>
            PublicRepliesRules.Check(request.TriggerType, request.TriggerConfig?.PublicReplies, context));
    }
}

public class UpdateFlowRequestValidator : AbstractValidator<UpdateFlowRequest>
{
    public UpdateFlowRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TriggerType).Must(t => t is "instagram_comment" or "instagram_dm")
            .WithMessage("triggerType бояд 'instagram_comment' ё 'instagram_dm' бошад.");
        RuleFor(x => x.TriggerConfig).NotNull();
        RuleFor(x => x).Custom((request, context) =>
            PublicRepliesRules.Check(request.TriggerType, request.TriggerConfig?.PublicReplies, context));
    }
}

public class FlowNodeInputValidator : AbstractValidator<FlowNodeInput>
{
    private static readonly string[] ValidTypes = Enum.GetNames<FlowNodeType>().Select(n => n.ToLowerInvariant()).ToArray();

    public FlowNodeInputValidator()
    {
        RuleFor(x => x.Type).Must(t => ValidTypes.Contains(t.ToLowerInvariant()))
            .WithMessage($"type бояд яке аз инҳо бошад: {string.Join(", ", ValidTypes)}.");
    }
}

public class UpdateFlowGraphRequestValidator : AbstractValidator<UpdateFlowGraphRequest>
{
    public UpdateFlowGraphRequestValidator()
    {
        RuleForEach(x => x.Nodes).SetValidator(new FlowNodeInputValidator());
        RuleFor(x => x).Custom((request, context) =>
        {
            var nodeIds = request.Nodes.Select(n => n.Id).ToHashSet();
            foreach (var edge in request.Edges)
            {
                if (!nodeIds.Contains(edge.FromNodeId) || !nodeIds.Contains(edge.ToNodeId))
                {
                    context.AddFailure(nameof(request.Edges), $"Edge {edge.Id} ба нодҳое ишора мекунад, ки дар граф нестанд.");
                    break;
                }
            }
        });
    }
}

/// <summary>A comment trigger may carry up to 5 short public replies; a DM trigger none (there is no comment).</summary>
internal static class PublicRepliesRules
{
    public static void Check<T>(string triggerType, string[]? replies, ValidationContext<T> context)
    {
        if (replies is null || replies.Length == 0)
            return;

        if (triggerType != "instagram_comment")
        {
            context.AddFailure("triggerConfig.publicReplies", "Ҷавоби зери пост танҳо барои триггери шарҳ аст.");
            return;
        }

        if (replies.Length > AutomationTriggerConfig.MaxPublicReplies)
            context.AddFailure("triggerConfig.publicReplies", $"На беш аз {AutomationTriggerConfig.MaxPublicReplies} вариант.");

        if (replies.Any(string.IsNullOrWhiteSpace))
            context.AddFailure("triggerConfig.publicReplies", "Варианти холӣ нагузоред.");

        if (replies.Any(r => r is not null && r.Trim().Length > AutomationTriggerConfig.MaxPublicReplyLength))
            context.AddFailure("triggerConfig.publicReplies", $"Ҳар вариант то {AutomationTriggerConfig.MaxPublicReplyLength} аломат.");
    }
}
