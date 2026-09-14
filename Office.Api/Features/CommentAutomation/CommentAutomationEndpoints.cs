using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Office.Api.Auth;
using Office.Api.Channels;
using Office.Api.Channels.Automation;
using Office.Api.Channels.Instagram;
using Office.Api.Channels.Meta;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;

namespace Office.Api.Features.CommentAutomation;

public static class CommentAutomationEndpoints
{
    private const string InstagramCommentTriggerType = "instagram_comment";

    public static IEndpointRouteBuilder MapCommentAutomationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/channels/{channelId:guid}").WithTags("CommentAutomation");

        group.MapGet("/automation-rules", ListAsync)
            .RequirePermission(Permissions.Channels.Manage)
            .WithSummary("Рӯйхати қоидаҳои автоматизатсияи коментарии Instagram")
            .Produces<IEnumerable<AutomationRuleListItem>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/automation-rules", CreateAsync)
            .WithValidation<CreateAutomationRuleRequest>()
            .RequirePermission(Permissions.Channels.Manage)
            .WithSummary("Сохтани қоидаи нав")
            .Produces<AutomationRuleListItem>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/automation-rules/{ruleId:guid}", UpdateAsync)
            .WithValidation<UpdateAutomationRuleRequest>()
            .RequirePermission(Permissions.Channels.Manage)
            .WithSummary("Таҳрири қоида")
            .Produces<AutomationRuleListItem>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/automation-rules/{ruleId:guid}/active", SetActiveAsync)
            .RequirePermission(Permissions.Channels.Manage)
            .WithSummary("Фаъол/ғайрифаъол кардани қоида")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/automation-rules/dry-run", DryRunAsync)
            .WithValidation<DryRunAutomationRuleRequest>()
            .RequirePermission(Permissions.Channels.Manage)
            .WithSummary("Санҷиши қоида бо матни фарзӣ — ҳеҷ чиз фиристода/захира намешавад")
            .Produces<DryRunAutomationRuleResult>(StatusCodes.Status200OK)
            .ProducesValidationProblem();

        group.MapGet("/instagram-media", ListInstagramMediaAsync)
            .RequirePermission(Permissions.Channels.Manage)
            .WithSummary("Постҳои охирини Instagram (барои интихоби пост дар қоида) — кэши 5 дақ")
            .Produces<InstagramMediaListResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListAsync(Guid channelId, AppDbContext db, CancellationToken ct)
    {
        if (!await db.Channels.AnyAsync(c => c.Id == channelId, ct))
            return Results.NotFound();

        var rules = await db.AutomationRules
            .Where(r => r.ChannelId == channelId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new { Rule = r, RunCount = db.AutomationRuns.Count(run => run.RuleId == r.Id) })
            .ToListAsync(ct);

        return Results.Ok(rules.Select(x => ToListItem(x.Rule, x.RunCount)));
    }

    private static async Task<IResult> CreateAsync(
        Guid channelId, CreateAutomationRuleRequest request, AppDbContext db, InstagramOAuthConnector instagramOAuth,
        IChannelCredentialsProtector protector, ILogger<Program> logger, CancellationToken ct)
    {
        var channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == channelId, ct);
        if (channel is null)
            return Results.NotFound();

        var rule = new AutomationRule
        {
            Id = Guid.CreateVersion7(),
            ChannelId = channelId,
            Name = request.Name,
            IsActive = true,
            TriggerType = InstagramCommentTriggerType,
            TriggerConfigJson = JsonSerializer.Serialize(request.TriggerConfig),
            ActionConfigJson = JsonSerializer.Serialize(request.ActionConfig),
            CooldownMinutes = request.CooldownMinutes,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.AutomationRules.Add(rule);

        // Каналҳои аллакай пайвастшуда пеш аз ин rule обунаи webhook-и "comments" надоштанд
        // (RequiredWebhookField-и қаблӣ танҳо "messages" буд) — ҳангоми сохтани АВВАЛИН rule-и
        // фаъол, обуна бори дигар (идемпотентӣ) фиристода мешавад, то коментарийҳо воқеан расанд.
        var isFirstActiveRule = !await db.AutomationRules.AnyAsync(r => r.ChannelId == channelId && r.IsActive, ct);
        if (isFirstActiveRule && !string.IsNullOrEmpty(channel.CredentialsEncrypted))
        {
            try
            {
                var credentials = InstagramCredentials.Parse(protector.Unprotect(channel.CredentialsEncrypted));
                channel.WebhookSetupWarning = await instagramOAuth.EnsureWebhookSubscriptionAsync(
                    credentials.InstagramAccountId, credentials.AccessToken, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Instagram: обунаи webhook барои канали {ChannelId} ҳангоми сохтани rule нашуд", channelId);
            }
        }

        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/channels/{channelId}/automation-rules/{rule.Id}", ToListItem(rule, 0));
    }

    private static async Task<IResult> UpdateAsync(
        Guid channelId, Guid ruleId, UpdateAutomationRuleRequest request, AppDbContext db, CancellationToken ct)
    {
        var rule = await db.AutomationRules.FirstOrDefaultAsync(r => r.Id == ruleId && r.ChannelId == channelId, ct);
        if (rule is null)
            return Results.NotFound();

        rule.Name = request.Name;
        rule.TriggerConfigJson = JsonSerializer.Serialize(request.TriggerConfig);
        rule.ActionConfigJson = JsonSerializer.Serialize(request.ActionConfig);
        rule.CooldownMinutes = request.CooldownMinutes;

        var runCount = await db.AutomationRuns.CountAsync(r => r.RuleId == rule.Id, ct);
        await db.SaveChangesAsync(ct);

        return Results.Ok(ToListItem(rule, runCount));
    }

    private static async Task<IResult> SetActiveAsync(
        Guid channelId, Guid ruleId, SetAutomationRuleActiveRequest request, AppDbContext db, CancellationToken ct)
    {
        var rule = await db.AutomationRules.FirstOrDefaultAsync(r => r.Id == ruleId && r.ChannelId == channelId, ct);
        if (rule is null)
            return Results.NotFound();

        rule.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static IResult DryRunAsync(Guid channelId, DryRunAutomationRuleRequest request)
    {
        var match = CommentAutomationMatcher.Match(request.TriggerConfig, request.CommentText, request.MediaId);
        return Results.Ok(new DryRunAutomationRuleResult(match.Matched, match.MatchedKeyword));
    }

    private static async Task<IResult> ListInstagramMediaAsync(
        Guid channelId, string? after, int? limit, AppDbContext db, InstagramProvider instagramProvider, IMemoryCache cache, CancellationToken ct)
    {
        var channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == channelId && c.Type == ChannelType.Instagram, ct);
        if (channel is null)
            return Results.NotFound();

        var cacheKey = $"ig-media:{channelId}:{after}:{limit ?? 25}";
        if (cache.TryGetValue(cacheKey, out InstagramMediaListResult? cached) && cached is not null)
            return Results.Ok(cached);

        var page = await instagramProvider.GetRecentMediaAsync(channel, after, limit ?? 25, ct);
        var result = new InstagramMediaListResult(
            page.Items.Select(i => new InstagramMediaListItem(i.Id, i.MediaType, i.ImageUrl, i.Permalink, i.Caption, i.Timestamp)).ToList(),
            page.NextCursor);

        cache.Set(cacheKey, result, TimeSpan.FromMinutes(5));
        return Results.Ok(result);
    }

    private static AutomationRuleListItem ToListItem(AutomationRule rule, int runCount) => new(
        rule.Id,
        rule.Name,
        rule.IsActive,
        rule.TriggerType,
        JsonSerializer.Deserialize<AutomationTriggerConfig>(rule.TriggerConfigJson)!,
        JsonSerializer.Deserialize<AutomationActionConfig>(rule.ActionConfigJson)!,
        rule.CooldownMinutes,
        rule.CreatedAt,
        runCount);
}
