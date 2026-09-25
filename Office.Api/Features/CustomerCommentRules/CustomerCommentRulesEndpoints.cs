using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Channels;
using Office.Api.Channels.Instagram;
using Office.Api.Channels.Meta;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Features.CommentAutomation;
using Office.Api.Features.CustomerComments;
using Office.Api.Features.Subscriptions;

namespace Office.Api.Features.CustomerCommentRules;

/// <summary>
/// A мизоҷ's comment auto-reply, on their comments page: the staff rules exactly (keywords and
/// posts, replies under the comment in turn, a Direct message, the follow check), through the
/// staff handlers (CommentAutomationEndpoints). The tenant filter scopes every query to the
/// caller, so another owner's channel or rule is simply not found. On top: CustomerOnly for every
/// route; creating a rule or switching one on needs a plan and room in its ActiveAutomations
/// limit (shared with the flows). Editing, switching off and deleting stay possible without a
/// plan — the rules just don't run (CommentAutomationProcessor checks the plan).
/// </summary>
public static class CustomerCommentRulesEndpoints
{
    public static IEndpointRouteBuilder MapCustomerCommentRulesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/public/channels/{channelId:guid}/automation-rules")
            .WithTags("CustomerCommentRules")
            .RequireAuthorization(AuthSchemes.CustomerOnlyPolicy);

        group.MapGet("/", CommentAutomationEndpoints.ListAsync)
            .WithSummary("Қоидаҳои автоҷавоби шарҳ дар канали худ")
            .Produces<IEnumerable<AutomationRuleListItem>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithValidation<CreateAutomationRuleRequest>()
            .WithSummary("Қоидаи нав (фаъол) — бо тариф, дар ҳадди ActiveAutomations")
            .Produces<AutomationRuleListItem>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{ruleId:guid}", CommentAutomationEndpoints.UpdateAsync)
            .WithValidation<UpdateAutomationRuleRequest>()
            .WithSummary("Таҳрири қоида")
            .Produces<AutomationRuleListItem>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/{ruleId:guid}/active", SetActiveAsync)
            .WithSummary("Фурӯзон/хомӯш — фурӯзон кардан ба ҳадди ActiveAutomations ҳисоб мешавад")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{ruleId:guid}", CommentAutomationEndpoints.DeleteAsync)
            .WithSummary("Нест кардани қоида")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/dry-run", DryRunAsync)
            .WithValidation<DryRunAutomationRuleRequest>()
            .RequireRateLimiting(CustomerCommentsEndpoints.ActionRateLimitPolicy)
            .WithSummary("Санҷиши қоида бо матни фарзӣ — ҳеҷ чиз фиристода намешавад")
            .Produces<DryRunAutomationRuleResult>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        Guid channelId, CreateAutomationRuleRequest request, ClaimsPrincipal principal, AppDbContext db,
        IConfiguration configuration, InstagramOAuthConnector instagramOAuth, IChannelCredentialsProtector protector,
        ILogger<Program> logger, CancellationToken ct)
    {
        // Another owner's channel is a 404 before any talk of plans.
        if (!await db.Channels.AnyAsync(c => c.Id == channelId && c.Type == ChannelType.Instagram, ct))
            return Results.NotFound();

        return await CustomerEntitlements.CheckCanActivateOneMoreAutomationAsync(principal.GetUserId(), db, configuration, ct)
            ?? await CommentAutomationEndpoints.CreateAsync(channelId, request, db, instagramOAuth, protector, logger, ct);
    }

    private static async Task<IResult> SetActiveAsync(
        Guid channelId, Guid ruleId, SetAutomationRuleActiveRequest request, ClaimsPrincipal principal, AppDbContext db,
        IConfiguration configuration, CancellationToken ct)
    {
        if (request.IsActive)
        {
            var isActive = await db.AutomationRules
                .Where(r => r.Id == ruleId && r.ChannelId == channelId)
                .Select(r => (bool?)r.IsActive)
                .FirstOrDefaultAsync(ct);
            if (isActive is null)
                return Results.NotFound();

            if (isActive == false &&
                await CustomerEntitlements.CheckCanActivateOneMoreAutomationAsync(principal.GetUserId(), db, configuration, ct) is { } denied)
                return denied;
        }

        return await CommentAutomationEndpoints.SetActiveAsync(channelId, ruleId, request, db, ct);
    }

    /// <summary>With the follow check on, this asks Instagram — a paid action like the rest.</summary>
    private static async Task<IResult> DryRunAsync(
        Guid channelId, DryRunAutomationRuleRequest request, ClaimsPrincipal principal, AppDbContext db,
        IConfiguration configuration, InstagramProvider instagramProvider, CancellationToken ct)
    {
        if (!await db.Channels.AnyAsync(c => c.Id == channelId, ct))
            return Results.NotFound();

        if (await CustomerEntitlements.LoadLimitsAsync(principal.GetUserId(), db, configuration, ct) is null)
            return CustomerEntitlements.NoAccessProblem();

        return await CommentAutomationEndpoints.DryRunAsync(channelId, request, db, instagramProvider, ct);
    }
}
