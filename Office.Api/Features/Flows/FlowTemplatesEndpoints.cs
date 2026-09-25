using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Channels.Flows;
using Office.Api.Channels.Instagram;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Media;

namespace Office.Api.Features.Flows;

public static class FlowTemplatesEndpoints
{
    public static IEndpointRouteBuilder MapFlowTemplatesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/flow-templates", ListAsync)
            .WithTags("FlowTemplates")
            .RequirePermission(Permissions.Channels.Manage)
            .WithSummary("Рӯйхати шаблонҳои омодаи flow")
            .Produces<IEnumerable<FlowTemplateListItem>>(StatusCodes.Status200OK);

        app.MapPost("/api/channels/{channelId:guid}/flows/from-template/{templateId:guid}", InstantiateAsync)
            .WithValidation<CreateFlowRequest>()
            .WithTags("FlowTemplates")
            .RequirePermission(Permissions.Channels.Manage)
            .WithSummary("Нусхабардории шаблон ба flow-и нави корбар")
            .Produces<FlowDetail>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    internal static async Task<IResult> ListAsync(AppDbContext db, CancellationToken ct) =>
        Results.Ok(await db.FlowTemplates.OrderBy(t => t.CreatedAt)
            .Select(t => new FlowTemplateListItem(t.Id, t.Name, t.Description)).ToListAsync(ct));

    internal static async Task<IResult> InstantiateAsync(
        Guid channelId, Guid templateId, CreateFlowRequest request, AppDbContext db,
        InstagramProvider instagramProvider, IMediaProcessor mediaProcessor, ILogger<Program> logger, CancellationToken ct)
    {
        var channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == channelId, ct);
        if (channel is null)
            return Results.NotFound();

        var template = await db.FlowTemplates.FirstOrDefaultAsync(t => t.Id == templateId, ct);
        if (template is null)
            return Results.NotFound();

        var definition = JsonSerializer.Deserialize<FlowTemplateDefinition>(template.DefinitionJson)!;

        var flow = new Flow
        {
            Id = Guid.CreateVersion7(),
            ChannelId = channelId,
            Name = request.Name,
            IsActive = true,
            TriggerType = request.TriggerType,
            TriggerConfigJson = JsonSerializer.Serialize(request.TriggerConfig),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Flows.Add(flow);

        var (templateNodes, templateEdges) = FlowTemplateInstantiator.Instantiate(definition, flow.Id);
        await FlowTemplateInstantiator.AttachDefaultImagesAsync(definition, templateNodes, channel, instagramProvider, mediaProcessor, logger, ct);
        db.FlowNodes.AddRange(templateNodes);
        db.FlowEdges.AddRange(templateEdges);

        await db.SaveChangesAsync(ct);

        var nodes = await db.FlowNodes.Where(n => n.FlowId == flow.Id).ToListAsync(ct);
        var edges = await db.FlowEdges.Where(e => e.FlowId == flow.Id).ToListAsync(ct);
        return Results.Created($"/api/flows/{flow.Id}", new FlowDetail(
            flow.Id, flow.ChannelId, flow.Name, flow.IsActive, flow.TriggerType, request.TriggerConfig,
            nodes.Select(n => new FlowNodeDto(n.Id, n.Type.ToString().ToLowerInvariant(), JsonDocument.Parse(n.ConfigJson).RootElement, n.X, n.Y)).ToList(),
            edges.Select(e => new FlowEdgeDto(e.Id, e.FromNodeId, e.FromPort, e.ToNodeId)).ToList(),
            flow.CreatedAt, flow.UpdatedAt));
    }
}
