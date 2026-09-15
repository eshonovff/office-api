using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Channels.Automation;
using Office.Api.Channels.Flows;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;

namespace Office.Api.Features.Flows;

public static class FlowsEndpoints
{
    public static IEndpointRouteBuilder MapFlowsEndpoints(this IEndpointRouteBuilder app)
    {
        var byChannel = app.MapGroup("/api/channels/{channelId:guid}/flows").WithTags("Flows");

        byChannel.MapGet("/", ListAsync)
            .RequirePermission(Permissions.Channels.Manage)
            .WithSummary("Рӯйхати flow-ҳои канал")
            .Produces<IEnumerable<FlowListItem>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        byChannel.MapPost("/", CreateAsync)
            .WithValidation<CreateFlowRequest>()
            .RequirePermission(Permissions.Channels.Manage)
            .WithSummary("Сохтани flow-и холӣ (граф баъдтар аз canvas сабт мешавад)")
            .Produces<FlowDetail>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        var byFlow = app.MapGroup("/api/flows/{id:guid}").WithTags("Flows");

        byFlow.MapGet("/", GetAsync)
            .RequirePermission(Permissions.Channels.Manage)
            .WithSummary("Маълумоти пурраи flow бо граф (nodes+edges)")
            .Produces<FlowDetail>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        byFlow.MapPut("/", UpdateAsync)
            .WithValidation<UpdateFlowRequest>()
            .RequirePermission(Permissions.Channels.Manage)
            .WithSummary("Навсозии ном/триггер")
            .Produces<FlowDetail>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        byFlow.MapPut("/graph", UpdateGraphAsync)
            .WithValidation<UpdateFlowGraphRequest>()
            .RequirePermission(Permissions.Channels.Manage)
            .WithSummary("Захираи пурраи граф аз canvas (autosave) — ҳамаи nodes+edges иваз мешаванд")
            .Produces<FlowDetail>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        byFlow.MapPatch("/active", SetActiveAsync)
            .RequirePermission(Permissions.Channels.Manage)
            .WithSummary("Фаъол/ғайрифаъол кардани flow")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        byFlow.MapDelete("/", DeleteAsync)
            .RequirePermission(Permissions.Channels.Manage)
            .WithSummary("Нест кардани flow (бо ҳамаи сессияҳояш)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        byFlow.MapGet("/stats", StatsAsync)
            .RequirePermission(Permissions.Channels.Manage)
            .WithSummary("Омори flow: шумораи сессияҳо аз рӯи ҳолат, контактҳо дар ҳар нод")
            .Produces<FlowStats>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListAsync(Guid channelId, AppDbContext db, CancellationToken ct)
    {
        if (!await db.Channels.AnyAsync(c => c.Id == channelId, ct))
            return Results.NotFound();

        var flows = await db.Flows
            .Where(f => f.ChannelId == channelId)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new FlowListItem(f.Id, f.ChannelId, f.Name, f.IsActive, f.TriggerType, f.Nodes.Count, f.CreatedAt, f.UpdatedAt))
            .ToListAsync(ct);

        return Results.Ok(flows);
    }

    private static async Task<IResult> CreateAsync(Guid channelId, CreateFlowRequest request, AppDbContext db, CancellationToken ct)
    {
        if (!await db.Channels.AnyAsync(c => c.Id == channelId, ct))
            return Results.NotFound();

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
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/flows/{flow.Id}", ToDetail(flow, [], []));
    }

    private static async Task<IResult> GetAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var flow = await db.Flows.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (flow is null)
            return Results.NotFound();

        var nodes = await db.FlowNodes.Where(n => n.FlowId == id).ToListAsync(ct);
        var edges = await db.FlowEdges.Where(e => e.FlowId == id).ToListAsync(ct);

        return Results.Ok(ToDetail(flow, nodes, edges));
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateFlowRequest request, AppDbContext db, CancellationToken ct)
    {
        var flow = await db.Flows.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (flow is null)
            return Results.NotFound();

        flow.Name = request.Name;
        flow.TriggerType = request.TriggerType;
        flow.TriggerConfigJson = JsonSerializer.Serialize(request.TriggerConfig);
        flow.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var nodes = await db.FlowNodes.Where(n => n.FlowId == id).ToListAsync(ct);
        var edges = await db.FlowEdges.Where(e => e.FlowId == id).ToListAsync(ct);
        return Results.Ok(ToDetail(flow, nodes, edges));
    }

    private static async Task<IResult> UpdateGraphAsync(Guid id, UpdateFlowGraphRequest request, AppDbContext db, CancellationToken ct)
    {
        var flow = await db.Flows.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (flow is null)
            return Results.NotFound();

        foreach (var node in request.Nodes)
        {
            var error = ValidateNodeConfig(node.Type, node.Config);
            if (error is not null)
                return Results.Problem(title: "Config-и нод нодуруст аст", detail: $"Нод {node.Id}: {error}", statusCode: StatusCodes.Status400BadRequest);
        }

        // Иваз кардани пурра — canvas ҳамеша ҳолати комили худро мефиристад (ниг. шарҳи
        // UpdateFlowGraphRequest барои сабаб). Соддатар ва бехатартар аз diff барои autosave.
        var existingNodes = await db.FlowNodes.Where(n => n.FlowId == id).ToListAsync(ct);
        var existingEdges = await db.FlowEdges.Where(e => e.FlowId == id).ToListAsync(ct);
        db.FlowEdges.RemoveRange(existingEdges);
        db.FlowNodes.RemoveRange(existingNodes);

        foreach (var node in request.Nodes)
        {
            db.FlowNodes.Add(new FlowNode
            {
                Id = node.Id,
                FlowId = id,
                Type = Enum.Parse<FlowNodeType>(node.Type, ignoreCase: true),
                ConfigJson = node.Config.GetRawText(),
                X = node.X,
                Y = node.Y,
            });
        }

        foreach (var edge in request.Edges)
        {
            db.FlowEdges.Add(new FlowEdge { Id = edge.Id, FlowId = id, FromNodeId = edge.FromNodeId, FromPort = edge.FromPort, ToNodeId = edge.ToNodeId });
        }

        flow.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Results.Ok(ToDetail(flow, await db.FlowNodes.Where(n => n.FlowId == id).ToListAsync(ct), await db.FlowEdges.Where(e => e.FlowId == id).ToListAsync(ct)));
    }

    private static async Task<IResult> SetActiveAsync(Guid id, SetFlowActiveRequest request, AppDbContext db, CancellationToken ct)
    {
        var flow = await db.Flows.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (flow is null)
            return Results.NotFound();

        flow.IsActive = request.IsActive;
        flow.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var flow = await db.Flows.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (flow is null)
            return Results.NotFound();

        db.Flows.Remove(flow); // Cascade: FlowNode/FlowEdge/FlowSession(→FlowSessionStep) ҳама бо OnDelete(Cascade)
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> StatsAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        if (!await db.Flows.AnyAsync(f => f.Id == id, ct))
            return Results.NotFound();

        var sessions = await db.FlowSessions.Where(s => s.FlowId == id)
            .Select(s => s.Status)
            .ToListAsync(ct);

        var nodeStats = await db.FlowSessionSteps
            .Where(s => s.Session.FlowId == id)
            .GroupBy(s => s.NodeId)
            .Select(g => new FlowNodeStat(g.Key, g.Select(x => x.SessionId).Distinct().Count()))
            .ToListAsync(ct);

        return Results.Ok(new FlowStats(
            TotalSessions: sessions.Count,
            FinishedSessions: sessions.Count(s => s == FlowSessionStatus.Finished),
            ActiveOrWaitingSessions: sessions.Count(s => s is FlowSessionStatus.Active or FlowSessionStatus.Waiting),
            FailedSessions: sessions.Count(s => s == FlowSessionStatus.Failed),
            Nodes: nodeStats));
    }

    /// <summary>Ҳар навъи нод config-и typed-и худро дорад — ниг. Channels/Flows/FlowConfigs.cs.</summary>
    private static string? ValidateNodeConfig(string type, JsonElement config)
    {
        try
        {
            switch (type.ToLowerInvariant())
            {
                case "message":
                    var message = config.Deserialize<MessageNodeConfig>(FlowJsonOptions.Options) ?? throw new JsonException("null");
                    // "payment" дар намуди JSON қабул карда мешавад (мутобиқат бо спека), вале
                    // қасдан рад мешавад — спека худаш "маҳсулоти пулакӣ"-ро дар НАГИР дорад.
                    if (message.Buttons.Any(b => b.Action == "payment"))
                        return "Тугмаи навъи 'payment' дастгирӣ намешавад.";
                    if (message.Buttons.Any(b => b.Action != MessageButton.ActionNext && b.Action != MessageButton.ActionUrl))
                        return "action-и тугма бояд 'next' ё 'url' бошад.";
                    break;
                case "condition":
                    _ = config.Deserialize<ConditionNodeConfig>(FlowJsonOptions.Options) ?? throw new JsonException("null");
                    break;
                case "action":
                    var action = config.Deserialize<ActionNodeConfig>(FlowJsonOptions.Options) ?? throw new JsonException("null");
                    if (string.IsNullOrEmpty(action.Kind))
                        return "kind лозим аст.";
                    break;
                case "note":
                    _ = config.Deserialize<NoteNodeConfig>(FlowJsonOptions.Options) ?? throw new JsonException("null");
                    break;
                default:
                    return $"Навъи нодуруст: {type}.";
            }
            return null;
        }
        catch (JsonException ex)
        {
            return $"config хонда нашуд: {ex.Message}";
        }
    }

    private static FlowDetail ToDetail(Flow flow, List<FlowNode> nodes, List<FlowEdge> edges) => new(
        flow.Id,
        flow.ChannelId,
        flow.Name,
        flow.IsActive,
        flow.TriggerType,
        JsonSerializer.Deserialize<AutomationTriggerConfig>(flow.TriggerConfigJson)!,
        nodes.Select(n => new FlowNodeDto(n.Id, n.Type.ToString().ToLowerInvariant(), JsonDocument.Parse(n.ConfigJson).RootElement, n.X, n.Y)).ToList(),
        edges.Select(e => new FlowEdgeDto(e.Id, e.FromNodeId, e.FromPort, e.ToNodeId)).ToList(),
        flow.CreatedAt,
        flow.UpdatedAt);
}
