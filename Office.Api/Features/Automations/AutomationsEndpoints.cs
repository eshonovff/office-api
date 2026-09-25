using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Common;
using Office.Api.Data;

namespace Office.Api.Features.Automations;

public static class AutomationsEndpoints
{
    public static IEndpointRouteBuilder MapAutomationsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/automations", ListAsync)
            .WithTags("Automations")
            .RequirePermission(Permissions.Channels.Manage)
            .WithSummary("Рӯйхати ягонаи автоматизатсияҳо (simple + flow) бо ҷустуҷӯ/сорт")
            .Produces<IEnumerable<AutomationListItem>>(StatusCodes.Status200OK);

        return app;
    }

    private static async Task<IResult> ListAsync(AppDbContext db, Guid? channelId, string? search, string? sort, CancellationToken ct)
    {
        var simpleQuery = db.AutomationRules.Include(r => r.Channel).AsQueryable();
        var flowQuery = db.Flows.Include(f => f.Channel).AsQueryable();
        if (channelId is not null)
        {
            simpleQuery = simpleQuery.Where(r => r.ChannelId == channelId);
            flowQuery = flowQuery.Where(f => f.ChannelId == channelId);
        }

        var simpleRules = await simpleQuery.ToListAsync(ct);
        var flows = await flowQuery.ToListAsync(ct);

        var simpleRunCounts = await db.AutomationRuns
            .Where(r => simpleRules.Select(sr => sr.Id).Contains(r.RuleId))
            .GroupBy(r => r.RuleId)
            .Select(g => new { RuleId = g.Key, ContactCount = g.Select(r => r.ActorExternalId).Distinct().Count() })
            .ToListAsync(ct);
        var simpleRunCountsByRule = simpleRunCounts.ToDictionary(x => x.RuleId, x => x.ContactCount);

        var flowSessionStats = await db.FlowSessions
            .Where(s => flows.Select(f => f.Id).Contains(s.FlowId))
            .GroupBy(s => s.FlowId)
            .Select(g => new
            {
                FlowId = g.Key,
                ContactCount = g.Select(s => s.ContactId).Distinct().Count(),
                Total = g.Count(),
                Finished = g.Count(s => s.Status == Data.Entities.FlowSessionStatus.Finished),
            })
            .ToListAsync(ct);
        var flowStatsByFlow = flowSessionStats.ToDictionary(x => x.FlowId);

        var items = new List<AutomationListItem>();
        items.AddRange(simpleRules.Select(r => new AutomationListItem(
            r.Id, "simple", r.Name, r.IsActive, r.ChannelId, r.Channel.Name,
            simpleRunCountsByRule.GetValueOrDefault(r.Id, 0), null, r.CreatedAt)));
        items.AddRange(flows.Select(f =>
        {
            var stats = flowStatsByFlow.GetValueOrDefault(f.Id);
            var conversion = stats is { Total: > 0 } ? Math.Round(100.0 * stats.Finished / stats.Total, 1) : (double?)null;
            return new AutomationListItem(f.Id, "flow", f.Name, f.IsActive, f.ChannelId, f.Channel.Name, stats?.ContactCount ?? 0, conversion, f.CreatedAt);
        }));

        if (!string.IsNullOrWhiteSpace(search))
            items = items.Where(i => i.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();

        items = sort switch
        {
            "name" => items.OrderBy(i => i.Name).ToList(),
            "conversion" => items.OrderByDescending(i => i.ConversionPercent ?? -1).ToList(),
            _ => items.OrderByDescending(i => i.CreatedAt).ToList(),
        };

        return Results.Ok(items);
    }
}
