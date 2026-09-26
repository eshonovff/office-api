using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Features.Conversations;
using Office.Api.Features.DataDeletion;
using Office.Api.Features.Subscriptions;

namespace Office.Api.Features.CustomerContacts;

/// <summary>
/// A мизоҷ's contacts — everyone who wrote to their Instagram (a conversation is a contact), with
/// the tags and details their flows collected. Who may see what is decided in one place: the
/// tenant filter on Conversation, ContactTag, ContactVariable and the rest (AppDbContext) — every
/// query here only ever finds the calling мизоҷ's rows, so another's id is simply not found
/// (404). Reading is free; changing tags or details and the export need a plan; deleting a
/// contact never does — a person's data can always be removed. See
/// docs/phases/phase-17-19-customer-growth.md for the threat table.
/// </summary>
public static class CustomerContactsEndpoints
{
    public const string ExportRateLimitPolicy = "customer-contacts-export";
    public const int MaxExportRows = 50_000;

    private const int DefaultPageSize = 30;
    private const int MaxPageSize = 100;
    private const int VariablesInList = 3;
    private const int TagsInFilter = 200;
    private const int AutomationsInCard = 20;

    public static IEndpointRouteBuilder MapCustomerContactsEndpoints(this IEndpointRouteBuilder app)
    {
        var contacts = app.MapGroup("/api/public/contacts")
            .WithTags("CustomerContacts")
            .RequireAuthorization(AuthSchemes.CustomerOnlyPolicy);

        contacts.MapGet("/", ListAsync)
            .WithSummary("Контактҳои мизоҷ — ҷустуҷӯ, тег, канал; саҳифабандӣ")
            .Produces<PagedResult<CustomerContactListItem>>(StatusCodes.Status200OK);

        contacts.MapGet("/tags", TagsAsync)
            .WithSummary("Тегҳои контактҳо бо шумора — барои филтр")
            .Produces<IReadOnlyList<ContactTagCount>>(StatusCodes.Status200OK);

        contacts.MapGet("/export", ExportAsync)
            .RequireRateLimiting(ExportRateLimitPolicy)
            .WithSummary("Контактҳо ба Excel (.xlsx) — бо ҳамон филтрҳо; тариф лозим")
            .Produces(StatusCodes.Status200OK, contentType: XlsxSheet.ContentType)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        contacts.MapGet("/{id:guid}", GetAsync)
            .WithSummary("Корти контакт")
            .Produces<CustomerContactDetail>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        contacts.MapPost("/{id:guid}/tags", AddTagAsync)
            .WithValidation<AddContactTagRequest>()
            .WithSummary("Илова кардани тег — тариф лозим")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        contacts.MapDelete("/{id:guid}/tags", RemoveTagAsync)
            .WithSummary("Нест кардани тег (?tag=) — тариф лозим")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        contacts.MapPut("/{id:guid}/variables", SetVariableAsync)
            .WithValidation<SetContactVariableRequest>()
            .WithSummary("Илова ё ислоҳи маълумот (калид — қимат) — тариф лозим")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        contacts.MapDelete("/{id:guid}/variables", RemoveVariableAsync)
            .WithSummary("Нест кардани маълумот (?key=) — тариф лозим")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        contacts.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Нест кардани контакт ва ҳамаи маълумоти ӯ (дар Instagram не) — бе тариф ҳам")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>
    /// The filters the list and the export share. The search is plain containment of the lowered
    /// text (no LIKE pattern — nothing typed can act as a wildcard); a leading "@" is ignored.
    /// </summary>
    public static IQueryable<Conversation> Filter(AppDbContext db, Guid? channelId, string? search, string? tag)
    {
        var query = db.Conversations.AsNoTracking().AsQueryable();
        if (channelId is not null)
            query = query.Where(c => c.ChannelId == channelId);

        var term = search?.Trim().TrimStart('@').ToLowerInvariant();
        if (!string.IsNullOrEmpty(term))
        {
            if (term.Length > ContactLimits.MaxSearchLength)
                term = term[..ContactLimits.MaxSearchLength];
            query = query.Where(c =>
                (c.ContactName != null && c.ContactName.ToLower().Contains(term)) ||
                (c.ContactUsername != null && c.ContactUsername.ToLower().Contains(term)));
        }

        var tagValue = tag?.Trim();
        if (!string.IsNullOrEmpty(tagValue))
            query = query.Where(c => db.ContactTags.Any(t => t.ContactId == c.Id && t.Tag == tagValue));

        return query;
    }

    public static async Task<IResult> ListAsync(
        Guid? channelId, string? search, string? tag, string? sort, int? page, int? pageSize, AppDbContext db, CancellationToken ct)
    {
        var query = Filter(db, channelId, search, tag);
        var resolvedPageSize = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);
        var resolvedPage = Math.Max(page ?? 1, 1);
        var totalCount = await query.CountAsync(ct);

        query = sort switch
        {
            "new" => query.OrderByDescending(c => c.CreatedAt),
            "name" => query.OrderBy(c => c.ContactName ?? c.ContactUsername).ThenBy(c => c.CreatedAt),
            _ => query.OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt).ThenByDescending(c => c.CreatedAt),
        };

        var rows = await query
            .Skip((resolvedPage - 1) * resolvedPageSize)
            .Take(resolvedPageSize)
            .Select(c => new ContactRow(c.Id, c.ChannelId, c.Channel.Type, c.Channel.Name, c.ContactName, c.ContactUsername,
                c.ContactAvatarUrl, c.CreatedAt, c.LastMessageAt, c.WindowExpiresAt))
            .ToListAsync(ct);

        var ids = rows.Select(r => r.Id).ToList();
        var tags = await LoadTagsAsync(db, ids, ct);
        var variables = await LoadVariablesAsync(db, ids, ct);
        var now = DateTimeOffset.UtcNow;

        var items = rows.Select(r => new CustomerContactListItem(
            r.Id, r.ChannelId, r.ChannelType.ToString(), r.ChannelName, r.Name, r.Username, r.AvatarUrl,
            tags.GetValueOrDefault(r.Id, []),
            variables.GetValueOrDefault(r.Id, []).Take(VariablesInList).ToList(),
            r.CreatedAt, r.LastMessageAt, OpenUntil(r.WindowExpiresAt, now))).ToList();

        return Results.Ok(new PagedResult<CustomerContactListItem>(items, totalCount, resolvedPage, resolvedPageSize));
    }

    public static async Task<IResult> TagsAsync(Guid? channelId, AppDbContext db, CancellationToken ct)
    {
        var tags = db.ContactTags.AsNoTracking().AsQueryable();
        if (channelId is not null)
            tags = tags.Where(t => t.Contact.ChannelId == channelId);

        // Counted in the database (GROUP BY), not by loading every tag of every contact.
        var counts = await tags
            .GroupBy(t => t.Tag)
            .Select(g => new { Tag = g.Key, Count = g.Count() })
            .OrderByDescending(t => t.Count).ThenBy(t => t.Tag)
            .Take(TagsInFilter)
            .ToListAsync(ct);
        return Results.Ok(counts.Select(t => new ContactTagCount(t.Tag, t.Count)).ToList());
    }

    public static async Task<IResult> GetAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var row = await db.Conversations.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new ContactRow(c.Id, c.ChannelId, c.Channel.Type, c.Channel.Name, c.ContactName, c.ContactUsername,
                c.ContactAvatarUrl, c.CreatedAt, c.LastMessageAt, c.WindowExpiresAt))
            .FirstOrDefaultAsync(ct);
        if (row is null)
            return Results.NotFound();

        var externalId = await db.Conversations.Where(c => c.Id == id).Select(c => c.ExternalId).FirstAsync(ct);
        var tags = await LoadTagsAsync(db, [id], ct);
        var variables = await LoadVariablesAsync(db, [id], ct);
        var messageCount = await db.Messages.CountAsync(m => m.ConversationId == id && !m.IsInternalNote, ct);
        var commentCount = await db.InstagramComments.CountAsync(c => c.ChannelId == row.ChannelId && c.AuthorExternalId == externalId, ct);

        var follow = await db.AutomationRuns
            .Where(r => r.Rule.ChannelId == row.ChannelId && r.ActorExternalId == externalId && r.FollowCheckResult != null)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new { r.FollowCheckResult, r.CreatedAt })
            .FirstOrDefaultAsync(ct);

        var automations = await db.FlowSessions
            .Where(s => s.ContactId == id)
            .OrderByDescending(s => s.CreatedAt)
            .Take(AutomationsInCard)
            .Select(s => new { s.FlowId, s.Flow.Name, s.Status, s.CreatedAt })
            .ToListAsync(ct);

        return Results.Ok(new CustomerContactDetail(
            row.Id, row.ChannelId, row.ChannelType.ToString(), row.ChannelName, row.Name, row.Username, row.AvatarUrl,
            tags.GetValueOrDefault(id, []), variables.GetValueOrDefault(id, []),
            row.CreatedAt, row.LastMessageAt, OpenUntil(row.WindowExpiresAt, DateTimeOffset.UtcNow),
            messageCount, commentCount,
            follow?.FollowCheckResult?.ToString(), follow?.CreatedAt,
            automations.Select(a => new ContactAutomationDto(a.FlowId, a.Name, a.Status.ToString(), a.CreatedAt)).ToList()));
    }

    public static async Task<IResult> AddTagAsync(
        Guid id, AddContactTagRequest request, ClaimsPrincipal principal, AppDbContext db, IConfiguration configuration, CancellationToken ct)
    {
        if (await FindOrPlanProblemAsync(id, principal, db, configuration, ct) is { } problem)
            return problem;

        var tag = request.Tag.Trim();
        var existing = await db.ContactTags.Where(t => t.ContactId == id).Select(t => t.Tag).ToListAsync(ct);
        if (existing.Contains(tag))
            return Results.NoContent();
        if (existing.Count >= ContactLimits.MaxTagsPerContact)
            return Results.Problem(detail: $"То {ContactLimits.MaxTagsPerContact} тег барои як контакт.", statusCode: StatusCodes.Status400BadRequest);

        db.ContactTags.Add(new ContactTag { ContactId = id, Tag = tag, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    public static async Task<IResult> RemoveTagAsync(
        Guid id, string? tag, ClaimsPrincipal principal, AppDbContext db, IConfiguration configuration, CancellationToken ct)
    {
        if (await FindOrPlanProblemAsync(id, principal, db, configuration, ct) is { } problem)
            return problem;

        var value = tag?.Trim();
        var row = await db.ContactTags.FirstOrDefaultAsync(t => t.ContactId == id && t.Tag == value, ct);
        if (row is not null)
        {
            db.ContactTags.Remove(row);
            await db.SaveChangesAsync(ct);
        }
        return Results.NoContent();
    }

    public static async Task<IResult> SetVariableAsync(
        Guid id, SetContactVariableRequest request, ClaimsPrincipal principal, AppDbContext db, IConfiguration configuration, CancellationToken ct)
    {
        if (await FindOrPlanProblemAsync(id, principal, db, configuration, ct) is { } problem)
            return problem;

        var key = request.Key.Trim();
        var value = request.Value.Trim();
        var variable = await db.ContactVariables.FirstOrDefaultAsync(v => v.ContactId == id && v.Key == key, ct);
        if (variable is not null)
        {
            variable.Value = value;
        }
        else
        {
            if (await db.ContactVariables.CountAsync(v => v.ContactId == id, ct) >= ContactLimits.MaxVariablesPerContact)
                return Results.Problem(detail: $"То {ContactLimits.MaxVariablesPerContact} майдон барои як контакт.", statusCode: StatusCodes.Status400BadRequest);
            db.ContactVariables.Add(new ContactVariable { ContactId = id, Key = key, Value = value });
        }

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    public static async Task<IResult> RemoveVariableAsync(
        Guid id, string? key, ClaimsPrincipal principal, AppDbContext db, IConfiguration configuration, CancellationToken ct)
    {
        if (await FindOrPlanProblemAsync(id, principal, db, configuration, ct) is { } problem)
            return problem;

        var value = key?.Trim();
        var row = await db.ContactVariables.FirstOrDefaultAsync(v => v.ContactId == id && v.Key == value, ct);
        if (row is not null)
        {
            db.ContactVariables.Remove(row);
            await db.SaveChangesAsync(ct);
        }
        return Results.NoContent();
    }

    public static async Task<IResult> ExportAsync(
        Guid? channelId, string? search, string? tag, string? lang, ClaimsPrincipal principal, AppDbContext db,
        IConfiguration configuration, ILogger<Program> logger, CancellationToken ct)
    {
        if (await CustomerEntitlements.LoadLimitsAsync(principal.GetUserId(), db, configuration, ct) is null)
            return CustomerEntitlements.NoAccessProblem();

        var query = Filter(db, channelId, search, tag);
        var rows = await query
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .Take(MaxExportRows)
            .Select(c => new { c.Id, c.ContactName, c.ContactUsername, ChannelName = c.Channel.Name, c.CreatedAt, c.LastMessageAt })
            .ToListAsync(ct);

        // Tags and details of the same filtered set — a subquery, not a list of ids in the SQL.
        var contactIds = query.Select(c => c.Id);
        var tags = (await db.ContactTags.Where(t => contactIds.Contains(t.ContactId)).OrderBy(t => t.CreatedAt)
                .Select(t => new { t.ContactId, t.Tag }).ToListAsync(ct))
            .GroupBy(t => t.ContactId).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(t => t.Tag).ToList());
        var variables = (await db.ContactVariables.Where(v => contactIds.Contains(v.ContactId))
                .Select(v => new { v.ContactId, v.Key, v.Value }).ToListAsync(ct))
            .GroupBy(v => v.ContactId).ToDictionary(g => g.Key, g => (IReadOnlyDictionary<string, string>)g.ToDictionary(v => v.Key, v => v.Value));

        var file = ContactXlsxWriter.Write(
            rows.Select(r => new ContactXlsxWriter.Row(
                r.ContactName, r.ContactUsername, r.ChannelName,
                tags.GetValueOrDefault(r.Id, []),
                r.CreatedAt, r.LastMessageAt,
                variables.GetValueOrDefault(r.Id, EmptyVariables))).ToList(),
            lang == "ru" ? ContactXlsxWriter.Russian : ContactXlsxWriter.Tajik,
            TimeSpan.FromHours(OfficeLocalDate.OfficeUtcOffsetHours));

        // Who and how many — never the content.
        logger.LogInformation("Contacts export by customer {CustomerId}: {Count} rows", principal.GetUserId(), rows.Count);

        var date = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(OfficeLocalDate.OfficeUtcOffsetHours)).ToString("yyyy-MM-dd");
        return Results.File(file, XlsxSheet.ContentType, $"contacts-{date}.xlsx");
    }

    public static async Task<IResult> DeleteAsync(
        Guid id, ClaimsPrincipal principal, AppDbContext db, IConfiguration configuration, IWebHostEnvironment env,
        ILogger<Program> logger, CancellationToken ct)
    {
        // Another мизоҷ's contact is invisible here (tenant filter) — "not found", nothing touched.
        if (!await db.Conversations.AnyAsync(c => c.Id == id, ct))
            return Results.NotFound();

        IReadOnlyList<string>? files;
        if (db.Database.IsRelational())
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            files = await ContactDataEraser.EraseAsync(db, id, ct);
            await transaction.CommitAsync(ct);
        }
        else
        {
            files = await ContactDataEraser.EraseAsync(db, id, ct);
        }

        if (files is null)
            return Results.NotFound();

        ContactDataEraser.DeleteFiles(UploadsPathResolver.ResolveRootPath(configuration, env), files);
        logger.LogInformation("Contact {ContactId} deleted by customer {CustomerId} ({FileCount} files)", id, principal.GetUserId(), files.Count);
        return Results.NoContent();
    }

    /// <summary>
    /// The shared gate of every change: the contact must be the caller's (else 404 — checked
    /// first, so nothing about another's contact leaks through a plan message), then a plan (403).
    /// </summary>
    private static async Task<IResult?> FindOrPlanProblemAsync(
        Guid id, ClaimsPrincipal principal, AppDbContext db, IConfiguration configuration, CancellationToken ct)
    {
        if (!await db.Conversations.AnyAsync(c => c.Id == id, ct))
            return Results.NotFound();
        if (await CustomerEntitlements.LoadLimitsAsync(principal.GetUserId(), db, configuration, ct) is null)
            return CustomerEntitlements.NoAccessProblem();
        return null;
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyVariables = new Dictionary<string, string>();

    private static async Task<Dictionary<Guid, IReadOnlyList<string>>> LoadTagsAsync(AppDbContext db, List<Guid> ids, CancellationToken ct) =>
        (await db.ContactTags.AsNoTracking().Where(t => ids.Contains(t.ContactId)).OrderBy(t => t.CreatedAt)
            .Select(t => new { t.ContactId, t.Tag }).ToListAsync(ct))
        .GroupBy(t => t.ContactId)
        .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(t => t.Tag).ToList());

    private static async Task<Dictionary<Guid, IReadOnlyList<ContactVariableDto>>> LoadVariablesAsync(AppDbContext db, List<Guid> ids, CancellationToken ct) =>
        (await db.ContactVariables.AsNoTracking().Where(v => ids.Contains(v.ContactId)).OrderBy(v => v.Key)
            .Select(v => new { v.ContactId, v.Key, v.Value }).ToListAsync(ct))
        .GroupBy(v => v.ContactId)
        .ToDictionary(g => g.Key, g => (IReadOnlyList<ContactVariableDto>)g.Select(v => new ContactVariableDto(v.Key, v.Value)).ToList());

    private static DateTimeOffset? OpenUntil(DateTimeOffset? windowExpiresAt, DateTimeOffset now) =>
        windowExpiresAt is { } until && until > now ? until : null;

    private sealed record ContactRow(
        Guid Id, Guid ChannelId, ChannelType ChannelType, string ChannelName, string? Name, string? Username, string? AvatarUrl,
        DateTimeOffset CreatedAt, DateTimeOffset? LastMessageAt, DateTimeOffset? WindowExpiresAt);
}
