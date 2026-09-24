using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Channels.Instagram;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Features.Flows;
using Office.Api.Features.Subscriptions;
using Office.Api.Media;

namespace Office.Api.Features.CustomerFlows;

/// <summary>
/// A мизоҷ's automations (Flow Builder, phase 14). The handlers are the staff ones
/// (FlowsEndpoints / FlowTemplatesEndpoints): they only ever read through AppDbContext, whose
/// tenant filter scopes every query to the caller — another owner's channel or flow id is simply
/// not found. On top: CustomerOnly for every route, and the plan checks (no access after expiry;
/// ActiveAutomations limit whenever a flow would become active: create, from-template, activate).
/// Editing an existing flow stays possible without access — running it doesn't (FlowEngine side).
/// </summary>
public static class CustomerFlowsEndpoints
{
    public static IEndpointRouteBuilder MapCustomerFlowsEndpoints(this IEndpointRouteBuilder app)
    {
        var byChannel = app.MapGroup("/api/public/channels/{channelId:guid}/flows")
            .WithTags("CustomerFlows")
            .RequireAuthorization(AuthSchemes.CustomerOnlyPolicy);

        byChannel.MapGet("/", FlowsEndpoints.ListAsync)
            .WithSummary("Автоматизатсияҳои канали худ")
            .Produces<IEnumerable<FlowListItem>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        byChannel.MapPost("/", CreateAsync)
            .WithValidation<CreateFlowRequest>()
            .WithSummary("Сохтани автоматизатсия (фаъол) — маҳдудияти ActiveAutomations-и тариф")
            .Produces<FlowDetail>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        byChannel.MapPost("/from-template/{templateId:guid}", CreateFromTemplateAsync)
            .WithValidation<CreateFlowRequest>()
            .WithSummary("Автоматизатсия аз шаблон (фаъол) — маҳдудияти ActiveAutomations-и тариф")
            .Produces<FlowDetail>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        byChannel.MapPost("/media", UploadMediaAsync)
            .DisableAntiforgery()
            .WithSummary("Боркунии медиа барои нодаи паём — бо тарифи фаъол ё триал")
            .Produces<UploadFlowMediaResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/api/public/flow-templates", FlowTemplatesEndpoints.ListAsync)
            .WithTags("CustomerFlows")
            .RequireAuthorization(AuthSchemes.CustomerOnlyPolicy)
            .WithSummary("Шаблонҳои омода")
            .Produces<IEnumerable<FlowTemplateListItem>>(StatusCodes.Status200OK);

        var byFlow = app.MapGroup("/api/public/flows/{id:guid}")
            .WithTags("CustomerFlows")
            .RequireAuthorization(AuthSchemes.CustomerOnlyPolicy);

        byFlow.MapGet("/", FlowsEndpoints.GetAsync)
            .WithSummary("Автоматизатсия бо граф")
            .Produces<FlowDetail>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        byFlow.MapPut("/", FlowsEndpoints.UpdateAsync)
            .WithValidation<UpdateFlowRequest>()
            .WithSummary("Ном/триггер")
            .Produces<FlowDetail>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        byFlow.MapPut("/graph", FlowsEndpoints.UpdateGraphAsync)
            .WithValidation<UpdateFlowGraphRequest>()
            .WithSummary("Захираи граф аз canvas")
            .Produces<FlowDetail>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        byFlow.MapPatch("/active", SetActiveAsync)
            .WithSummary("Фурӯзон/хомӯш — фурӯзон кардан ба маҳдудияти ActiveAutomations ҳисоб мешавад")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        byFlow.MapDelete("/", FlowsEndpoints.DeleteAsync)
            .WithSummary("Нест кардан")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        byFlow.MapGet("/stats", FlowsEndpoints.StatsAsync)
            .WithSummary("Омор")
            .Produces<FlowStats>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        Guid channelId, CreateFlowRequest request, ClaimsPrincipal principal, AppDbContext db,
        IConfiguration configuration, CancellationToken ct)
    {
        return await CheckCanActivateOneMoreAsync(principal, db, configuration, ct)
            ?? await FlowsEndpoints.CreateAsync(channelId, request, db, ct);
    }

    private static async Task<IResult> CreateFromTemplateAsync(
        Guid channelId, Guid templateId, CreateFlowRequest request, ClaimsPrincipal principal, AppDbContext db,
        IConfiguration configuration, InstagramProvider instagramProvider,
        IMediaProcessor mediaProcessor, ILogger<Program> logger, CancellationToken ct)
    {
        return await CheckCanActivateOneMoreAsync(principal, db, configuration, ct)
            ?? await FlowTemplatesEndpoints.InstantiateAsync(
                channelId, templateId, request, db, instagramProvider, mediaProcessor, logger, ct);
    }

    private static async Task<IResult> UploadMediaAsync(
        Guid channelId, IFormFile file, ClaimsPrincipal principal, AppDbContext db, IConfiguration configuration,
        InstagramProvider instagramProvider, IMediaProcessor mediaProcessor,
        ILogger<Program> logger, CancellationToken ct)
    {
        if (await CustomerEntitlements.LoadLimitsAsync(principal.GetUserId(), db, configuration, ct) is null)
            return CustomerEntitlements.NoAccessProblem();

        return await FlowsEndpoints.UploadMediaAsync(channelId, file, db, instagramProvider, mediaProcessor, logger, ct);
    }

    private static async Task<IResult> SetActiveAsync(
        Guid id, SetFlowActiveRequest request, ClaimsPrincipal principal, AppDbContext db,
        IConfiguration configuration, CancellationToken ct)
    {
        if (request.IsActive)
        {
            var isActive = await db.Flows.Where(f => f.Id == id).Select(f => (bool?)f.IsActive).FirstOrDefaultAsync(ct);
            if (isActive is null)
                return Results.NotFound();

            if (isActive == false && await CheckCanActivateOneMoreAsync(principal, db, configuration, ct) is { } denied)
                return denied;
        }

        return await FlowsEndpoints.SetActiveAsync(id, request, db, ct);
    }

    /// <summary>
    /// Null when one more active automation fits the caller's plan; otherwise the 403 to return.
    /// Counts the caller's active flows only — the tenant filter scopes the count.
    /// </summary>
    private static async Task<IResult?> CheckCanActivateOneMoreAsync(
        ClaimsPrincipal principal, AppDbContext db, IConfiguration configuration, CancellationToken ct)
    {
        var limits = await CustomerEntitlements.LoadLimitsAsync(principal.GetUserId(), db, configuration, ct);
        if (limits is null)
            return CustomerEntitlements.NoAccessProblem();

        var active = await db.Flows.CountAsync(f => f.IsActive, ct);
        return CustomerEntitlements.CanAddOneMore(limits.ActiveAutomations, active)
            ? null
            : CustomerEntitlements.LimitReachedProblem(
                $"Тарифи шумо то {limits.ActiveAutomations} автоматизатсияи фаъол иҷозат медиҳад. " +
                "Якеашро хомӯш кунед ё тарифро баланд кунед.");
    }
}
