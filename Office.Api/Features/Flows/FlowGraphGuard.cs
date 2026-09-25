using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Office.Api.Channels.Flows;
using Office.Api.Data;
using Office.Api.Data.Entities;

namespace Office.Api.Features.Flows;

/// <summary>
/// Checks on a graph save that need the database — the request validator already guarantees
/// every edge points at a node inside the same request. Shared by staff and мизоҷ endpoints.
/// </summary>
public static class FlowGraphGuard
{
    /// <summary>The error to return, or null when the graph may be saved.</summary>
    public static async Task<IResult?> CheckAsync(Flow flow, UpdateFlowGraphRequest request, AppDbContext db, CancellationToken ct)
    {
        // goto_flow may only target a flow on the same channel: the contact belongs to this
        // channel. The caller's tenant filter already hides other owners' flows; the channel
        // condition also rules out the caller's own other channels.
        var gotoTargets = request.Nodes
            .Where(n => string.Equals(n.Type, "action", StringComparison.OrdinalIgnoreCase))
            .Select(n => n.Config.Deserialize<ActionNodeConfig>(FlowJsonOptions.Options))
            .Where(c => c?.Kind == ActionNodeConfig.KindGotoFlow && c.TargetFlowId is not null)
            .Select(c => c!.TargetFlowId!.Value)
            .Distinct()
            .ToList();

        if (gotoTargets.Count > 0)
        {
            var found = await db.Flows.CountAsync(f => gotoTargets.Contains(f.Id) && f.ChannelId == flow.ChannelId, ct);
            if (found != gotoTargets.Count)
            {
                return Results.Problem(
                    title: "Flow-и мақсад ёфт нашуд",
                    detail: "Амали \"гузариш ба flow\" танҳо ба flow-и ҳамин канал ишора карда метавонад.",
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }

        // Node/edge ids come from the canvas. One already used by ANOTHER flow (any owner — hence
        // IgnoreQueryFilters) would otherwise fail the insert on the primary key with a 500.
        var nodeIds = request.Nodes.Select(n => n.Id).ToList();
        var edgeIds = request.Edges.Select(e => e.Id).ToList();
        var idTaken =
            await db.FlowNodes.IgnoreQueryFilters().AnyAsync(n => nodeIds.Contains(n.Id) && n.FlowId != flow.Id, ct) ||
            await db.FlowEdges.IgnoreQueryFilters().AnyAsync(e => edgeIds.Contains(e.Id) && e.FlowId != flow.Id, ct);
        if (idTaken)
        {
            return Results.Problem(
                title: "ID-и такрорӣ",
                detail: "Баъзе нодҳо ё пайвандҳо ID-и flow-и дигарро доранд. Саҳифаро аз нав бор кунед.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return null;
    }
}
