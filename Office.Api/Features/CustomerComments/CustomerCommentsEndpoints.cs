using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Office.Api.Auth;
using Office.Api.Channels;
using Office.Api.Channels.Comments;
using Office.Api.Channels.Instagram;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Features.CommentAutomation;
using Office.Api.Features.Conversations;
using Office.Api.Features.Subscriptions;
using Office.Api.Realtime;

namespace Office.Api.Features.CustomerComments;

/// <summary>
/// A мизоҷ's Instagram comments: see them per post, reply under them, send the one Direct message
/// Meta allows, hide or delete. Who may touch what is decided by the tenant filter on
/// InstagramComment and Channel (AppDbContext): every lookup here only finds the caller's own rows,
/// and every action names a row by OUR id — Meta's comment and media ids and the channel's token
/// are read from the database, never taken from the request. Seeing is free; every action needs a
/// plan and is rate-limited. See docs/phases/phase-16-comments.md for the threat table.
/// </summary>
public static partial class CustomerCommentsEndpoints
{
    public const string ActionRateLimitPolicy = "customer-comment-action";

    /// <summary>Meta: one private reply per comment, within 7 days of it.</summary>
    public static readonly TimeSpan DirectWindow = TimeSpan.FromDays(7);

    private static readonly TimeSpan SyncCooldown = TimeSpan.FromMinutes(5);
    private const int MaxCommentsPerPost = 1000;
    private const int MaxSyncedTopLevel = 300;

    [GeneratedRegex("^[0-9_]{1,64}$")]
    private static partial Regex MediaIdFormat();

    public static IEndpointRouteBuilder MapCustomerCommentsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/public/comments")
            .WithTags("CustomerComments")
            .RequireAuthorization(AuthSchemes.CustomerOnlyPolicy);

        group.MapGet("/posts", ListPostsAsync)
            .WithSummary("Постҳои Instagram-и канали худ бо шумораи шарҳҳо ва шарҳҳои нав")
            .Produces<CustomerCommentPostsResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/", ListAsync)
            .WithSummary("Шарҳҳои як пост (бо ҷавобҳо), аз нав ба кӯҳна")
            .Produces<IReadOnlyList<CustomerCommentDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/new-count", NewCountAsync)
            .WithSummary("Шумораи шарҳҳои нави мухлисон — барои нишонаи меню")
            .Produces<NewCommentsResponse>(StatusCodes.Status200OK);

        group.MapPost("/read", MarkPostReadAsync)
            .WithValidation<CommentsOfPostRequest>()
            .WithSummary("Ҳамаи шарҳҳои пост хонда шуд")
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/sync", SyncAsync)
            .WithValidation<CommentsOfPostRequest>()
            .RequireRateLimiting(ActionRateLimitPolicy)
            .WithSummary("Гирифтани шарҳҳои пешинаи пост аз Instagram (на зудтар аз 1 бор дар 5 дақ)")
            .Produces<CommentSyncResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/reply", ReplyAsync)
            .WithValidation<CommentTextRequest>()
            .RequireRateLimiting(ActionRateLimitPolicy)
            .WithSummary("Ҷавоб зери шарҳ")
            .Produces<CustomerCommentDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/direct", SendDirectAsync)
            .WithValidation<CommentTextRequest>()
            .RequireRateLimiting(ActionRateLimitPolicy)
            .WithSummary("Паём ба Direct-и муаллифи шарҳ — як бор, дар 7 рӯз")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/hide", SetHiddenAsync)
            .RequireRateLimiting(ActionRateLimitPolicy)
            .WithSummary("Пинҳон кардан ё баргардондани шарҳи мухлис")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .RequireRateLimiting(ActionRateLimitPolicy)
            .WithSummary("Нест кардани шарҳ дар Instagram — барнагарданда")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> ListPostsAsync(
        Guid channelId, string? after, AppDbContext db, InstagramProvider instagram, IMemoryCache cache, CancellationToken ct)
    {
        var channel = await FindOwnInstagramChannelAsync(db, channelId, ct);
        if (channel is null)
            return Results.NotFound();
        if (NeedsReconnect(channel))
            return ReconnectProblem();

        InstagramMediaListResult page;
        try
        {
            page = await CommentAutomationEndpoints.LoadMediaPageAsync(channel, after, 25, instagram, cache, ct);
        }
        catch (GraphApiException ex)
        {
            return MetaProblem(ex);
        }

        var mediaIds = page.Items.Select(i => i.Id).ToList();
        var counts = await db.InstagramComments.AsNoTracking()
            .Where(c => c.ChannelId == channel.Id && !c.IsOwn && mediaIds.Contains(c.MediaExternalId))
            .GroupBy(c => c.MediaExternalId)
            .Select(g => new { MediaId = g.Key, Total = g.Count(), New = g.Count(c => !c.IsRead) })
            .ToDictionaryAsync(x => x.MediaId, ct);

        var items = page.Items.Select(i => new CustomerCommentPost(
            i.Id, i.MediaType, i.ImageUrl, i.Permalink, i.Caption, i.Timestamp,
            counts.TryGetValue(i.Id, out var c) ? c.Total : 0,
            counts.TryGetValue(i.Id, out var n) ? n.New : 0)).ToList();

        return Results.Ok(new CustomerCommentPostsResult(items, page.NextCursor));
    }

    private static async Task<IResult> ListAsync(Guid channelId, string mediaId, AppDbContext db, CancellationToken ct)
    {
        if (!MediaIdFormat().IsMatch(mediaId ?? ""))
            return Results.Problem(title: "Пости нодуруст", statusCode: StatusCodes.Status400BadRequest);
        if (await FindOwnInstagramChannelAsync(db, channelId, ct) is null)
            return Results.NotFound();

        var rows = await db.InstagramComments.AsNoTracking()
            .Where(c => c.ChannelId == channelId && c.MediaExternalId == mediaId)
            .OrderByDescending(c => c.CommentedAt)
            .Take(MaxCommentsPerPost)
            .ToListAsync(ct);

        var idByExternal = rows.ToDictionary(r => r.ExternalId, r => r.Id);
        var now = DateTimeOffset.UtcNow;
        return Results.Ok(rows.Select(r => ToDto(r, idByExternal, now)).ToList());
    }

    private static async Task<IResult> NewCountAsync(AppDbContext db, CancellationToken ct) =>
        Results.Ok(new NewCommentsResponse(await db.InstagramComments.CountAsync(c => !c.IsRead && !c.IsOwn, ct)));

    private static async Task<IResult> MarkPostReadAsync(
        CommentsOfPostRequest request, AppDbContext db, ICommentEventPublisher events, CancellationToken ct)
    {
        if (await FindOwnInstagramChannelAsync(db, request.ChannelId, ct) is null)
            return Results.NotFound();

        var changed = await db.InstagramComments
            .Where(c => c.ChannelId == request.ChannelId && c.MediaExternalId == request.MediaId && !c.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsRead, true), ct);
        if (changed > 0)
            await events.CommentsChangedAsync(request.ChannelId, request.MediaId, ct);
        return Results.NoContent();
    }

    /// <summary>
    /// Brings in comments the webhooks never delivered (older ones, or from before the account was
    /// connected). The post id comes from the request, but the call runs on this мизоҷ's own channel
    /// token — Meta itself refuses a post that is not theirs. Synced comments count as already read.
    /// </summary>
    private static async Task<IResult> SyncAsync(
        CommentsOfPostRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        InstagramProvider instagram,
        IMemoryCache cache,
        IConfiguration configuration,
        ICommentEventPublisher events,
        CancellationToken ct)
    {
        var channel = await FindOwnInstagramChannelAsync(db, request.ChannelId, ct);
        if (channel is null)
            return Results.NotFound();
        if (await CustomerEntitlements.LoadLimitsAsync(principal.GetUserId(), db, configuration, ct) is null)
            return CustomerEntitlements.NoAccessProblem();
        if (NeedsReconnect(channel))
            return ReconnectProblem();

        var throttleKey = $"comments-sync:{channel.Id}:{request.MediaId}";
        if (cache.TryGetValue(throttleKey, out _))
            return Results.Ok(new CommentSyncResult(0, Throttled: true));
        cache.Set(throttleKey, true, SyncCooldown);

        IReadOnlyList<InstagramCommentItem> remote;
        try
        {
            remote = await instagram.GetCommentsAsync(channel, request.MediaId, MaxSyncedTopLevel, ct);
        }
        catch (GraphApiException ex)
        {
            return MetaProblem(ex);
        }

        var existing = await db.InstagramComments
            .Where(c => c.ChannelId == channel.Id && c.MediaExternalId == request.MediaId)
            .ToDictionaryAsync(c => c.ExternalId, ct);

        var now = DateTimeOffset.UtcNow;
        var added = 0;
        var addedIds = new HashSet<string>();
        foreach (var item in remote)
        {
            if (existing.TryGetValue(item.Id, out var known))
            {
                known.IsHidden = item.Hidden;
                continue;
            }
            if (!addedIds.Add(item.Id))
                continue; // Meta listed the same comment twice

            var isOwn = item.AuthorId == channel.ExternalId;
            db.InstagramComments.Add(new InstagramComment
            {
                Id = Guid.CreateVersion7(),
                ChannelId = channel.Id,
                ExternalId = item.Id,
                MediaExternalId = request.MediaId,
                ParentExternalId = item.ParentId,
                AuthorExternalId = item.AuthorId,
                AuthorUsername = item.AuthorUsername,
                Text = item.Text,
                CommentedAt = item.Timestamp ?? now,
                ReceivedAt = now,
                IsOwn = isOwn,
                IsHidden = item.Hidden,
                IsRead = true,
            });
            added++;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // A webhook stored one of these at the same moment; the next sync picks up the rest.
            return Results.Ok(new CommentSyncResult(0, Throttled: false));
        }

        if (added > 0)
            await events.CommentsChangedAsync(channel.Id, request.MediaId, ct);
        return Results.Ok(new CommentSyncResult(added, Throttled: false));
    }

    private static async Task<IResult> ReplyAsync(
        Guid id,
        CommentTextRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        InstagramProvider instagram,
        IConfiguration configuration,
        ICommentEventPublisher events,
        CancellationToken ct)
    {
        var (comment, problem) = await LoadForActionAsync(id, principal, db, configuration, ct);
        if (problem is not null)
            return problem;

        // Instagram threads are one level deep: a reply to a reply goes under the top comment.
        var target = comment!.ParentExternalId ?? comment.ExternalId;
        var text = request.Text.Trim();
        string? replyId;
        try
        {
            replyId = await instagram.ReplyToCommentAsync(comment.Channel, target, text, ct);
        }
        catch (GraphApiException ex)
        {
            return MetaProblem(ex);
        }

        var now = DateTimeOffset.UtcNow;
        InstagramComment? reply = null;
        if (replyId is not null && !await db.InstagramComments.AnyAsync(c => c.ChannelId == comment.ChannelId && c.ExternalId == replyId, ct))
        {
            reply = new InstagramComment
            {
                Id = Guid.CreateVersion7(),
                ChannelId = comment.ChannelId,
                ExternalId = replyId,
                MediaExternalId = comment.MediaExternalId,
                ParentExternalId = target,
                AuthorExternalId = comment.Channel.ExternalId,
                AuthorUsername = comment.Channel.Name,
                Text = text,
                CommentedAt = now,
                ReceivedAt = now,
                IsOwn = true,
                IsRead = true,
            };
            db.InstagramComments.Add(reply);
        }
        comment.IsRead = true;
        await db.SaveChangesAsync(ct);
        await events.CommentsChangedAsync(comment.ChannelId, comment.MediaExternalId, ct);

        var idByExternal = new Dictionary<string, Guid> { [comment.ExternalId] = comment.Id };
        return reply is null
            ? Results.NoContent()
            : Results.Created($"/api/public/comments/{reply.Id}", ToDto(reply, idByExternal, now));
    }

    /// <summary>
    /// The one Direct message Meta allows per comment. Claimed atomically first (only a fan's
    /// comment, within 7 days, not yet messaged), so a double click, two tabs or an automation at
    /// the same moment can never send two; if Meta refuses, the claim is released.
    /// </summary>
    private static async Task<IResult> SendDirectAsync(
        Guid id,
        CommentTextRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        InstagramProvider instagram,
        IConfiguration configuration,
        ICommentEventPublisher events,
        IInboxEventPublisher inbox,
        CancellationToken ct)
    {
        var (comment, problem) = await LoadForActionAsync(id, principal, db, configuration, ct);
        if (problem is not null)
            return problem;

        var now = DateTimeOffset.UtcNow;
        var cutoff = now - DirectWindow;
        var claimed = await db.InstagramComments
            .Where(c => c.Id == id && !c.IsOwn && c.PrivateReplySentAt == null && c.CommentedAt > cutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.PrivateReplySentAt, now), ct);
        if (claimed == 0)
        {
            var detail = comment!.IsOwn ? "Ба шарҳи худатон Direct фиристода намешавад."
                : comment.PrivateReplySentAt is not null ? "Ба ин шарҳ аллакай Direct фиристода шудааст — Instagram танҳо як бор иҷозат медиҳад."
                : "Аз шарҳ 7 рӯз гузашт — Instagram дигар Direct-ро иҷозат намедиҳад.";
            return Results.Problem(title: "Direct фиристода намешавад", detail: detail, statusCode: StatusCodes.Status409Conflict);
        }

        var text = request.Text.Trim();
        try
        {
            await instagram.SendPrivateReplyAsync(comment!.Channel, comment.ExternalId, text, button: null, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Not sent (Meta refused, or the network failed): release the claim so it can be tried again.
            await db.InstagramComments
                .Where(c => c.Id == id && c.PrivateReplySentAt == now)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.PrivateReplySentAt, (DateTimeOffset?)null), CancellationToken.None);
            if (ex is GraphApiException meta)
                return MetaProblem(meta);
            throw;
        }

        // The message also shows in the мизоҷ's Chats, in the conversation with that fan.
        var senderName = await db.Customers.AsNoTracking()
            .Where(c => c.Id == principal.GetUserId()).Select(c => c.FullName).FirstAsync(ct);
        var conversation = await db.Conversations
            .FirstOrDefaultAsync(c => c.ChannelId == comment.ChannelId && c.ExternalId == comment.AuthorExternalId, ct);
        if (conversation is null)
        {
            conversation = new Conversation
            {
                Id = Guid.CreateVersion7(),
                ChannelId = comment.ChannelId,
                ExternalId = comment.AuthorExternalId,
                ContactName = comment.AuthorUsername,
                ContactUsername = comment.AuthorUsername,
                CreatedAt = now,
            };
            db.Conversations.Add(conversation);
        }
        conversation.LastMessageAt = now;
        var message = new Message
        {
            Id = Guid.CreateVersion7(),
            ConversationId = conversation.Id,
            Direction = MessageDirection.Outbound,
            Type = MessageType.Text,
            Body = text,
            DeliveryStatus = MessageDeliveryStatus.Sent,
            SentByUserName = senderName,
            CreatedAt = now,
        };
        db.Messages.Add(message);
        comment.IsRead = true;
        await db.SaveChangesAsync(ct);

        await inbox.MessageSentAsync(comment.ChannelId, assignedTo: null, MessageDto.FromEntity(message), ct);
        await events.CommentsChangedAsync(comment.ChannelId, comment.MediaExternalId, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SetHiddenAsync(
        Guid id,
        HideCommentRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        InstagramProvider instagram,
        IConfiguration configuration,
        ICommentEventPublisher events,
        CancellationToken ct)
    {
        var (comment, problem) = await LoadForActionAsync(id, principal, db, configuration, ct);
        if (problem is not null)
            return problem;
        if (comment!.IsOwn)
            return Results.Problem(title: "Шарҳи худатон пинҳон намешавад", statusCode: StatusCodes.Status409Conflict);

        try
        {
            await instagram.SetCommentHiddenAsync(comment.Channel, comment.ExternalId, request.Hidden, ct);
        }
        catch (GraphApiException ex)
        {
            return MetaProblem(ex);
        }

        comment.IsHidden = request.Hidden;
        comment.IsRead = true;
        await db.SaveChangesAsync(ct);
        await events.CommentsChangedAsync(comment.ChannelId, comment.MediaExternalId, ct);
        return Results.NoContent();
    }

    /// <summary>Irreversible on Instagram; its replies go with it there, so here too.</summary>
    private static async Task<IResult> DeleteAsync(
        Guid id,
        ClaimsPrincipal principal,
        AppDbContext db,
        InstagramProvider instagram,
        IConfiguration configuration,
        ICommentEventPublisher events,
        ILogger<InstagramComment> logger,
        CancellationToken ct)
    {
        var (comment, problem) = await LoadForActionAsync(id, principal, db, configuration, ct);
        if (problem is not null)
            return problem;

        try
        {
            await instagram.DeleteCommentAsync(comment!.Channel, comment.ExternalId, ct);
        }
        catch (GraphApiException ex)
        {
            return MetaProblem(ex);
        }

        var replies = await db.InstagramComments
            .Where(c => c.ChannelId == comment.ChannelId && c.ParentExternalId == comment.ExternalId)
            .ToListAsync(ct);
        db.InstagramComments.RemoveRange(replies);
        db.InstagramComments.Remove(comment);
        await db.SaveChangesAsync(ct);

        // Who deleted what and when — ids only, never the text.
        logger.LogInformation(
            "Customer {CustomerId} deleted comment {CommentId} on channel {ChannelId}", principal.GetUserId(), comment.Id, comment.ChannelId);
        await events.CommentsChangedAsync(comment.ChannelId, comment.MediaExternalId, ct);
        return Results.NoContent();
    }

    /// <summary>
    /// The shared gate of every action: the comment must be the caller's (tenant filter → 404),
    /// there must be a plan (403), and the Instagram connection must be usable (409).
    /// </summary>
    private static async Task<(InstagramComment? Comment, IResult? Problem)> LoadForActionAsync(
        Guid id, ClaimsPrincipal principal, AppDbContext db, IConfiguration configuration, CancellationToken ct)
    {
        var comment = await db.InstagramComments.Include(c => c.Channel).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (comment is null)
            return (null, Results.NotFound());
        if (await CustomerEntitlements.LoadLimitsAsync(principal.GetUserId(), db, configuration, ct) is null)
            return (null, CustomerEntitlements.NoAccessProblem());
        if (NeedsReconnect(comment.Channel))
            return (null, ReconnectProblem());
        return (comment, null);
    }

    private static Task<Channel?> FindOwnInstagramChannelAsync(AppDbContext db, Guid channelId, CancellationToken ct) =>
        db.Channels.FirstOrDefaultAsync(c => c.Id == channelId && c.Type == ChannelType.Instagram, ct);

    private static bool NeedsReconnect(Channel channel) => !channel.IsActive || channel.RequiresReconnect;

    private static CustomerCommentDto ToDto(InstagramComment c, IReadOnlyDictionary<string, Guid> idByExternal, DateTimeOffset now)
    {
        var directUntil = c.CommentedAt + DirectWindow;
        return new CustomerCommentDto(
            c.Id,
            c.ParentExternalId is not null && idByExternal.TryGetValue(c.ParentExternalId, out var parentId) ? parentId : null,
            c.AuthorUsername,
            c.IsOwn,
            c.PostedByAutomation,
            c.Text,
            c.CommentedAt,
            c.IsHidden,
            c.IsRead,
            DirectSent: c.PrivateReplySentAt is not null,
            CanSendDirect: !c.IsOwn && c.PrivateReplySentAt is null && directUntil > now,
            directUntil,
            c.AutoReplyError);
    }

    private static IResult ReconnectProblem() => Results.Problem(
        title: "Аккаунт пайваст нест",
        detail: "Instagram-ро дар бахши «Автоматизатсия» аз нав пайваст кунед.",
        statusCode: StatusCodes.Status409Conflict,
        extensions: new Dictionary<string, object?> { ["code"] = "reconnect" });

    private static IResult MetaProblem(GraphApiException ex) => Results.Problem(
        title: "Instagram рад кард",
        detail: ex.Message,
        statusCode: StatusCodes.Status409Conflict,
        extensions: new Dictionary<string, object?> { ["code"] = "meta_error" });
}
