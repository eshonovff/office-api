using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Channels;
using Office.Api.Channels.WhatsApp;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Permissions = Office.Api.Auth.Permissions;

namespace Office.Api.Features.Channels;

public static class ChannelsEndpoints
{
    public static IEndpointRouteBuilder MapChannelsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/channels").WithTags("Channels");

        group.MapGet("/", ListAsync)
            .RequirePermission(Permissions.Channels.Manage)
            .WithSummary("Рӯйхати каналҳо")
            .Produces<IEnumerable<ChannelListItem>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/mine", ListMineAsync)
            .RequirePermission(Permissions.Inbox.View)
            .WithSummary("Рӯйхати каналҳое, ки корбар барои Inbox дастрасӣ дорад — на channels.manage, барои JoinChannel-и realtime")
            .Produces<IEnumerable<ChannelSummary>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", GetAsync)
            .RequirePermission(Permissions.Inbox.Assign)
            .WithSummary("Маълумоти пурраи канал бо аъзо (бе credentials) — барои assign-by-drag дар /inbox")
            .Produces<ChannelDetail>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/whatsapp-templates", GetWhatsAppTemplatesAsync)
            .RequirePermission(Permissions.Inbox.Reply)
            .WithSummary("Рӯйхати шаблонҳои тасдиқшудаи WhatsApp аз Meta")
            .Produces<IEnumerable<WhatsAppTemplateInfo>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithValidation<CreateChannelRequest>()
            .RequirePermission(Permissions.Channels.Manage)
            .WithSummary("Сохтани канали нав — credentials фавран шифр мешаванд")
            .Produces<ChannelDetail>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/{id:guid}", UpdateAsync)
            .WithValidation<UpdateChannelRequest>()
            .RequirePermission(Permissions.Channels.Manage)
            .WithSummary("Навсозии ном, ҳолати фаъол ва (агар дода шавад) credentials")
            .Produces<ChannelDetail>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .RequirePermission(Permissions.Channels.Manage)
            .WithSummary("Ғайрифаъол кардани канал (soft — таърихи чат мемонад)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}/members", SetMembersAsync)
            .WithValidation<SetChannelMembersRequest>()
            .RequirePermission(Permissions.Channels.Manage)
            .WithSummary("Танзими рӯйхати аъзои канал")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListAsync(AppDbContext db, CancellationToken ct)
    {
        var channels = await db.Channels.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);
        return Results.Ok(channels.Select(ToListItem));
    }

    private static async Task<IResult> ListMineAsync(
        ClaimsPrincipal principal, AppDbContext db, IChannelAccessGuard access, CancellationToken ct)
    {
        var query = await access.ApplyChannelAccessFilterAsync(db.Channels.AsNoTracking(), principal, ct);
        var channels = await query.OrderBy(c => c.Name).ToListAsync(ct);
        return Results.Ok(channels.Select(ToSummary));
    }

    private static async Task<IResult> GetAsync(
        Guid id, ClaimsPrincipal principal, AppDbContext db, IChannelAccessGuard access, CancellationToken ct)
    {
        if (!await access.CanAccessChannelAsync(principal, id, ct))
            return Results.NotFound();

        var channel = await db.Channels.AsNoTracking()
            .Include(c => c.Members).ThenInclude(m => m.User)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        return channel is null ? Results.NotFound() : Results.Ok(ToDetail(channel));
    }

    private static async Task<IResult> GetWhatsAppTemplatesAsync(
        Guid id, ClaimsPrincipal principal, AppDbContext db, IChannelAccessGuard access, IChannelProviderFactory factory, CancellationToken ct)
    {
        if (!await access.CanAccessChannelAsync(principal, id, ct))
            return Results.NotFound();

        var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (channel is null)
            return Results.NotFound();

        var provider = factory.GetProvider(channel.Type);
        var templates = await provider.GetApprovedTemplatesAsync(channel, ct);
        return Results.Ok(templates);
    }

    private static async Task<IResult> CreateAsync(
        CreateChannelRequest request, AppDbContext db, IChannelCredentialsProtector protector, CancellationToken ct)
    {
        var type = Enum.Parse<ChannelType>(request.Type, ignoreCase: true);

        var exists = await db.Channels.AnyAsync(c => c.Type == type && c.ExternalId == request.ExternalId, ct);
        if (exists)
        {
            return Results.Problem(
                title: "Канал аллакай мавҷуд аст",
                detail: "Канали ҳамин навъ ва external_id аллакай сабт шудааст.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var channel = new Channel
        {
            Id = Guid.CreateVersion7(),
            Type = type,
            Name = request.Name,
            ExternalId = request.ExternalId,
            CredentialsEncrypted = protector.Protect(request.Credentials),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Channels.Add(channel);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/channels/{channel.Id}", ToDetail(channel));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, UpdateChannelRequest request, AppDbContext db, IChannelCredentialsProtector protector, CancellationToken ct)
    {
        var channel = await db.Channels.Include(c => c.Members).ThenInclude(m => m.User).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (channel is null)
            return Results.NotFound();

        channel.Name = request.Name;
        channel.IsActive = request.IsActive;

        if (!string.IsNullOrEmpty(request.Credentials))
            channel.CredentialsEncrypted = protector.Protect(request.Credentials);

        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDetail(channel));
    }

    private static async Task<IResult> DeleteAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (channel is null)
            return Results.NotFound();

        channel.IsActive = false;
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static async Task<IResult> SetMembersAsync(
        Guid id, SetChannelMembersRequest request, AppDbContext db, CancellationToken ct)
    {
        var channel = await db.Channels.Include(c => c.Members).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (channel is null)
            return Results.NotFound();

        var userIds = request.UserIds.Distinct().ToList();
        var existingUserCount = await db.Users.CountAsync(u => userIds.Contains(u.Id), ct);
        if (existingUserCount != userIds.Count)
        {
            return Results.Problem(
                title: "Корбари нодуруст",
                detail: "Яке аз корбарон вуҷуд надорад.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        channel.Members.Clear();
        foreach (var userId in userIds)
            channel.Members.Add(new ChannelMember { ChannelId = channel.Id, UserId = userId });

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static ChannelListItem ToListItem(Channel channel) => new(
        channel.Id, channel.Type.ToString(), channel.Name, channel.ExternalId, channel.IsActive, channel.CreatedAt);

    private static ChannelSummary ToSummary(Channel channel) => new(
        channel.Id, channel.Type.ToString(), channel.Name, channel.IsActive);

    private static ChannelDetail ToDetail(Channel channel) => new(
        channel.Id,
        channel.Type.ToString(),
        channel.Name,
        channel.ExternalId,
        channel.IsActive,
        channel.CreatedAt,
        channel.Members.Select(m => new ChannelMemberDto(m.UserId, m.User.FullName, m.User.Username)).ToList());
}
