using System.Security.Cryptography;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Channels;
using Office.Api.Channels.Meta;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Features.Channels;
using Office.Api.Features.Subscriptions;

namespace Office.Api.Features.CustomerChannels;

public record CustomerChannelDto(
    Guid Id,
    string Type,
    string Name,
    bool IsActive,
    bool RequiresReconnect,
    string? WebhookSetupWarning,
    DateTimeOffset CreatedAt)
{
    public static CustomerChannelDto From(Channel c) => new(
        c.Id, c.Type.ToString(), c.Name, c.IsActive, c.RequiresReconnect, c.WebhookSetupWarning, c.CreatedAt);
}

/// <summary>
/// A мизоҷ's own channels (phase 14): list, connect Instagram via OAuth, disconnect. Every
/// endpoint is CustomerOnly and sees only the caller's channels through AppDbContext's tenant
/// filter. The OAuth callback is the shared anonymous one in ChannelOAuthEndpoints — the signed
/// state carries "мизоҷ + id", and connect only accepts a session started by the same мизоҷ.
/// Instagram only for now (phase 14 scope).
/// </summary>
public static class CustomerChannelsEndpoints
{
    private const int StateLifetimeMinutes = 10;
    private const string Provider = "instagram";

    public static IEndpointRouteBuilder MapCustomerChannelsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/public/channels")
            .WithTags("CustomerChannels")
            .RequireAuthorization(AuthSchemes.CustomerOnlyPolicy);

        group.MapGet("/", ListAsync)
            .WithSummary("Каналҳои худи мизоҷ")
            .Produces<IEnumerable<CustomerChannelDto>>(StatusCodes.Status200OK);

        group.MapGet("/oauth/instagram/start", StartAsync)
            .WithSummary("URL-и авторизатсияи Instagram — танҳо бо тарифи фаъол ё триал")
            .Produces<OAuthStartResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/oauth/instagram/connect", ConnectAsync)
            .WithValidation<ConnectChannelRequest>()
            .WithSummary("Пайвасти аккаунти интихобшуда — маҳдудияти аккаунтҳои тариф; аккаунти каси дигар → 409")
            .Produces<CustomerChannelDto>(StatusCodes.Status201Created)
            .Produces<CustomerChannelDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}", DisconnectAsync)
            .WithSummary("Ҷудо кардани канал — токен нест мешавад, автоматизатсияҳо қатъ мешаванд, маълумот мемонад")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListAsync(AppDbContext db, CancellationToken ct)
    {
        var channels = await db.Channels.AsNoTracking().OrderBy(c => c.CreatedAt).ToListAsync(ct);
        return Results.Ok(channels.Select(CustomerChannelDto.From));
    }

    private static async Task<IResult> StartAsync(
        ClaimsPrincipal principal,
        AppDbContext db,
        IConfiguration configuration,
        IChannelOAuthConnectorFactory connectorFactory,
        CancellationToken ct)
    {
        var customerId = principal.GetUserId();
        if (await LoadLimitsAsync(customerId, db, configuration, ct) is null)
            return CustomerEntitlements.NoAccessProblem();

        var redirectUri = ChannelOAuthEndpoints.BuildRedirectUri(configuration, Provider);
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(StateLifetimeMinutes);
        var state = OAuthStateCodec.Encode(
            Provider, customerId, nonce, expiresAt, ChannelOAuthEndpoints.GetStateSigningKey(configuration),
            OAuthOwnerKind.Customer);

        var url = connectorFactory.GetConnector(ChannelType.Instagram).BuildAuthorizationUrl(redirectUri, state);
        return Results.Ok(new OAuthStartResponse(url));
    }

    private static async Task<IResult> ConnectAsync(
        ConnectChannelRequest request,
        ClaimsPrincipal principal,
        IOAuthConnectionStore connectionStore,
        FacebookOAuthConnector facebookConnector,
        InstagramOAuthConnector instagramConnector,
        AppDbContext db,
        IChannelCredentialsProtector protector,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var customerId = principal.GetUserId();

        var session = connectionStore.TryGet(request.ConnectionId, DateTimeOffset.UtcNow);
        var account = session is null
            ? null
            : OAuthConnectPolicy.ResolveAccount(
                session, ChannelType.Instagram, customerId, request.ExternalId, OAuthOwnerKind.Customer);
        if (account is null)
            return ChannelOAuthEndpoints.ConnectionExpiredProblem();

        var limits = await LoadLimitsAsync(customerId, db, configuration, ct);
        if (limits is null)
            return CustomerEntitlements.NoAccessProblem();

        // Reconnecting an account the мизоҷ already has (e.g. after its token expired) never
        // counts against the limit — only a channel that would become active on top of the others.
        var own = await db.Channels.FirstOrDefaultAsync(
            c => c.Type == ChannelType.Instagram && c.ExternalId == account.ExternalId, ct);
        if (own is null || !own.IsActive)
        {
            var activeCount = await db.Channels.CountAsync(c => c.IsActive, ct);
            if (!CustomerEntitlements.CanAddOneMore(limits.Accounts, activeCount))
            {
                return CustomerEntitlements.LimitReachedProblem(
                    $"Тарифи шумо то {limits.Accounts} аккаунт иҷозат медиҳад. Аккаунти дигарро ҷудо кунед ё тарифро баланд кунед.");
            }
        }

        var result = await OAuthChannelConnection.SaveAsync(
            ChannelType.Instagram, account, request.Name, customerId, db, protector, facebookConnector, instagramConnector, ct);

        return result.Outcome switch
        {
            ChannelConnectOutcome.OwnedByAnother => OAuthChannelConnection.OwnedByAnotherProblem(),
            ChannelConnectOutcome.Created => Results.Created(
                $"/api/public/channels/{result.Channel!.Id}", CustomerChannelDto.From(result.Channel)),
            _ => Results.Ok(CustomerChannelDto.From(result.Channel!)),
        };
    }

    private static async Task<IResult> DisconnectAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        // Tenant filter: another owner's channel id is simply not found.
        var channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (channel is null)
            return Results.NotFound();

        // Soft: conversations and flows stay (reconnecting the same account brings them back), but
        // the token is dropped at once — nothing can be sent on this account's behalf any more.
        channel.IsActive = false;
        channel.CredentialsEncrypted = null;
        channel.CredentialsExpiresAt = null;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<PlanLimitsOptions?> LoadLimitsAsync(
        Guid customerId, AppDbContext db, IConfiguration configuration, CancellationToken ct)
    {
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId, ct);
        if (customer is null)
            return null;

        var access = CustomerAccessResolver.Resolve(
            DateTimeOffset.UtcNow, customer.TrialEndsAt, customer.PlanTier, customer.PlanExpiresAt);
        return CustomerEntitlements.ResolveLimits(access, SubscriptionCatalog.Load(configuration));
    }
}
