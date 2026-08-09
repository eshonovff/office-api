using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Permissions = Office.Api.Auth.Permissions;

namespace Office.Api.Features.Conversations;

public static class ConversationsEndpoints
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapConversationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/conversations").WithTags("Conversations");

        group.MapGet("/", ListAsync)
            .RequirePermission(Permissions.Inbox.View)
            .WithSummary("Рӯйхати чатҳо — филтр бо канал/статус/корманди таъиншуда, тартиб бо паёми охирин")
            .Produces<PagedResult<ConversationListItem>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", GetAsync)
            .RequirePermission(Permissions.Inbox.View)
            .WithSummary("Маълумоти пурраи чат")
            .Produces<ConversationDetail>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListAsync(
        Guid? channelId,
        string? status,
        Guid? assignedUserId,
        int? page,
        int? pageSize,
        AppDbContext db,
        CancellationToken ct)
    {
        var query = db.Conversations.AsNoTracking()
            .Include(c => c.Channel)
            .Include(c => c.Assignee)
            .AsQueryable();

        if (channelId is not null)
            query = query.Where(c => c.ChannelId == channelId);

        if (!string.IsNullOrEmpty(status))
        {
            if (!Enum.TryParse<ConversationStatus>(status, ignoreCase: true, out var parsedStatus))
            {
                return Results.Problem(
                    title: "Статуси нодуруст",
                    detail: "Параметри `status` нодуруст аст.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            query = query.Where(c => c.Status == parsedStatus);
        }

        if (assignedUserId is not null)
            query = query.Where(c => c.AssignedTo == assignedUserId);

        var (resolvedPage, resolvedPageSize) = ResolvePaging(page, pageSize);

        var totalCount = await query.CountAsync(ct);
        var conversations = await query
            .OrderByDescending(c => c.LastMessageAt)
            .ThenByDescending(c => c.CreatedAt)
            .Skip((resolvedPage - 1) * resolvedPageSize)
            .Take(resolvedPageSize)
            .ToListAsync(ct);

        var items = conversations.Select(ToListItem).ToList();
        return Results.Ok(new PagedResult<ConversationListItem>(items, totalCount, resolvedPage, resolvedPageSize));
    }

    private static async Task<IResult> GetAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var conversation = await db.Conversations.AsNoTracking()
            .Include(c => c.Channel)
            .Include(c => c.Assignee)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        return conversation is null ? Results.NotFound() : Results.Ok(ToDetail(conversation));
    }

    private static (int Page, int PageSize) ResolvePaging(int? page, int? pageSize)
    {
        var resolvedPage = page is > 0 ? page.Value : 1;
        var resolvedPageSize = pageSize switch
        {
            null or <= 0 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => pageSize.Value,
        };

        return (resolvedPage, resolvedPageSize);
    }

    private static ConversationListItem ToListItem(Conversation c) => new(
        c.Id, c.ChannelId, c.Channel.Type.ToString(), c.Channel.Name, c.ExternalId,
        c.ContactName, c.ContactAvatarUrl, c.Status.ToString(), c.AssignedTo, c.Assignee?.FullName,
        c.LastMessageAt, c.UnreadCount, c.WindowExpiresAt, c.CreatedAt);

    private static ConversationDetail ToDetail(Conversation c) => new(
        c.Id, c.ChannelId, c.Channel.Type.ToString(), c.Channel.Name, c.ExternalId,
        c.ContactName, c.ContactAvatarUrl, c.Status.ToString(), c.AssignedTo, c.Assignee?.FullName,
        c.LastMessageAt, c.UnreadCount, c.WindowExpiresAt, c.CreatedAt);
}
