using FluentValidation;
using Office.Api.Channels.Flows;
using Office.Api.Data.Entities;
using Office.Api.Features.CommentAutomation;

namespace Office.Api.Features.Flows;

public class CreateFlowRequestValidator : AbstractValidator<CreateFlowRequest>
{
    public CreateFlowRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TriggerType).Must(t => FlowTriggerTypes.All.Contains(t))
            .WithMessage($"triggerType бояд яке аз инҳо бошад: {string.Join(", ", FlowTriggerTypes.All)}.");
        RuleFor(x => x.TriggerConfig).NotNull().SetValidator(new AutomationTriggerConfigValidator());
        RuleFor(x => x).Custom((request, context) =>
        {
            if (request.TriggerConfig is not null && FlowTriggerTypes.CheckConfig(request.TriggerType, request.TriggerConfig) is { } problem)
                context.AddFailure(nameof(request.TriggerConfig), problem);
        });
    }
}

public class UpdateFlowRequestValidator : AbstractValidator<UpdateFlowRequest>
{
    public UpdateFlowRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TriggerType).Must(t => FlowTriggerTypes.All.Contains(t))
            .WithMessage($"triggerType бояд яке аз инҳо бошад: {string.Join(", ", FlowTriggerTypes.All)}.");
        RuleFor(x => x.TriggerConfig).NotNull().SetValidator(new AutomationTriggerConfigValidator());
        RuleFor(x => x).Custom((request, context) =>
        {
            if (request.TriggerConfig is not null && FlowTriggerTypes.CheckConfig(request.TriggerType, request.TriggerConfig) is { } problem)
                context.AddFailure(nameof(request.TriggerConfig), problem);
        });
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
