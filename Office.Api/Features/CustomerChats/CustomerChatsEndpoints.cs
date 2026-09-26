using System.Security.Claims;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Channels;
using Office.Api.Channels.WhatsApp;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Features.Conversations;
using Office.Api.Features.Subscriptions;
using Office.Api.Media;
using Office.Api.Realtime;

namespace Office.Api.Features.CustomerChats;

/// <summary>
/// A мизоҷ's own Instagram chats: read, reply, mark read. Who may see what is decided in one
/// place — the tenant query filter on Conversation/Message (AppDbContext): every query here
/// only ever finds the calling мизоҷ's chats, so another's id is simply "not found" (404).
/// Reading needs only a мизоҷ session (it is their own data); replying also needs a plan.
/// See docs/phases/phase-15-customer-chats.md for the threat table.
/// </summary>
public static class CustomerChatsEndpoints
{
    public const string SendRateLimitPolicy = "customer-chat-send";
    public const string MediaBase = "/api/public/messages";

    /// <summary>
    /// What a мизоҷ may send in a chat — what Instagram delivers (checked live 2026-09-26), and
    /// nothing a browser would run if the file were opened (no SVG, no HTML). WEBP/HEIC are
    /// converted to JPEG by MediaSendJob before upload. Each maps to the extension it is stored with.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> SendableMediaTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/gif"] = ".gif",
        ["image/webp"] = ".webp",
        ["image/heic"] = ".heic",
        ["image/heif"] = ".heif",
        ["video/mp4"] = ".mp4",
        ["video/quicktime"] = ".mov",
        ["audio/mp4"] = ".m4a",
        ["audio/x-m4a"] = ".m4a",
        ["audio/aac"] = ".aac",
        ["audio/mpeg"] = ".mp3",
        ["application/pdf"] = ".pdf",
    };

    /// <summary>
    /// What a browser records a voice note in: Chrome and Edge WebM/Opus, Firefox Ogg/Opus, Safari
    /// AAC in MP4. MediaSendJob always runs it through ffmpeg to AAC for Instagram — a file that is
    /// not really audio stops there. Stored with its own extension (".mp4", never ".m4a", which
    /// only the transcode writes).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> VoiceNoteTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["audio/webm"] = ".webm",
        ["audio/ogg"] = ".ogg",
        ["audio/mp4"] = ".mp4",
    };

    private const int DefaultPageSize = 30;
    private const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapCustomerChatsEndpoints(this IEndpointRouteBuilder app)
    {
        var chats = app.MapGroup("/api/public/conversations")
            .WithTags("CustomerChats")
            .RequireAuthorization(AuthSchemes.CustomerOnlyPolicy);

        chats.MapGet("/", ListAsync)
            .WithSummary("Чатҳои мизоҷ — аз ҳамаи каналҳои худаш, аз нав ба кӯҳна")
            .Produces<PagedResult<CustomerConversationListItem>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        chats.MapGet("/unread-count", UnreadCountAsync)
            .WithSummary("Шумораи чатҳое, ки паёми хонданашуда доранд — барои нишонаи меню")
            .Produces<UnreadChatsResponse>(StatusCodes.Status200OK);

        chats.MapGet("/{id:guid}", GetAsync)
            .WithSummary("Як чат")
            .Produces<CustomerConversationDetail>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        chats.MapGet("/{id:guid}/messages", ListMessagesAsync)
            .WithSummary("Паёмҳои чат — аз нав ба кӯҳна, саҳифабандӣшуда")
            .Produces<PagedResult<MessageDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        chats.MapPost("/{id:guid}/messages", SendAsync)
            .WithValidation<SendCustomerMessageRequest>()
            .RequireRateLimiting(SendRateLimitPolicy)
            .WithSummary("Ҷавоби матнӣ — бо таъхири бекоркунӣ (мисли кормандон); тариф лозим")
            .Produces<MessageDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        chats.MapPost("/{id:guid}/media", SendMediaAsync)
            .DisableAntiforgery()
            .RequireRateLimiting(SendRateLimitPolicy)
            .WithSummary("Фиристодани сурат, видео, овоз ё PDF — тариф лозим")
            .Produces<MessageDto>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        chats.MapPost("/{id:guid}/voice-note", SendVoiceNoteAsync)
            .DisableAntiforgery()
            .RequireRateLimiting(SendRateLimitPolicy)
            .WithSummary("Фиристодани паёми овозӣ, ки дар браузер сабт шуд — тариф лозим")
            .Produces<MessageDto>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        chats.MapPost("/{id:guid}/messages/{messageId:guid}/cancel", CancelAsync)
            .WithSummary("Бекор кардани ҷавоб, то он фиристода нашудааст")
            .Produces<MessageDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        chats.MapPost("/{id:guid}/read", MarkAsReadAsync)
            .WithSummary("Ҳамаи паёмҳои чат хонда шуд")
            .Produces<CustomerConversationDetail>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        var media = app.MapGroup(MediaBase)
            .WithTags("CustomerChats")
            .RequireAuthorization(AuthSchemes.CustomerOnlyPolicy);

        media.MapGet("/{messageId:guid}/media", DownloadMediaAsync)
            .WithSummary("Файли медиаи паём (расм/овоз/видео inline, ҳуҷҷат — замима)")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status410Gone)
            .ProducesProblem(StatusCodes.Status424FailedDependency);

        media.MapGet("/{messageId:guid}/thumbnail", DownloadThumbnailAsync)
            .WithSummary("Thumbnail-и расм/видео")
            .Produces(StatusCodes.Status200OK, contentType: "image/jpeg")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListAsync(
        Guid? channelId, string? search, bool? unreadOnly, int? page, int? pageSize, AppDbContext db, CancellationToken ct)
    {
        // The tenant filter already limits this to the caller's channels: a foreign channelId
        // just yields an empty page, never another мизоҷ's chats.
        var query = db.Conversations.AsNoTracking().AsQueryable();

        if (channelId is not null)
            query = query.Where(c => c.ChannelId == channelId);
        if (unreadOnly == true)
            query = query.Where(c => c.UnreadCount > 0);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(c =>
                EF.Functions.ILike(c.ContactName ?? "", pattern) || EF.Functions.ILike(c.ContactUsername ?? "", pattern));
        }

        var (resolvedPage, resolvedPageSize) = ResolvePaging(page, pageSize);
        var totalCount = await query.CountAsync(ct);

        var rows = await query
            .OrderByDescending(c => c.LastMessageAt)
            .ThenByDescending(c => c.CreatedAt)
            .Skip((resolvedPage - 1) * resolvedPageSize)
            .Take(resolvedPageSize)
            .Select(c => new
            {
                Conversation = c,
                c.Channel.Type,
                ChannelName = c.Channel.Name,
                Last = c.Messages
                    .Where(m => !m.IsInternalNote)
                    .OrderByDescending(m => m.CreatedAt)
                    .Select(m => new { m.Type, m.Direction, m.Body })
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new CustomerConversationListItem(
            r.Conversation.Id, r.Conversation.ChannelId, r.Type.ToString(), r.ChannelName,
            r.Conversation.ContactName, r.Conversation.ContactAvatarUrl, r.Conversation.ContactUsername,
            r.Conversation.LastMessageAt,
            r.Last is null ? null : new CustomerLastMessage(r.Last.Type.ToString(), r.Last.Direction.ToString(), r.Last.Body),
            r.Conversation.UnreadCount, r.Conversation.WindowExpiresAt, r.Conversation.CreatedAt)).ToList();

        return Results.Ok(new PagedResult<CustomerConversationListItem>(items, totalCount, resolvedPage, resolvedPageSize));
    }

    private static async Task<IResult> UnreadCountAsync(AppDbContext db, CancellationToken ct) =>
        Results.Ok(new UnreadChatsResponse(await db.Conversations.CountAsync(c => c.UnreadCount > 0, ct)));

    private static async Task<IResult> GetAsync(
        Guid id, ClaimsPrincipal principal, AppDbContext db, IConfiguration configuration, CancellationToken ct)
    {
        var conversation = await db.Conversations.AsNoTracking().Include(c => c.Channel).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (conversation is null)
            return Results.NotFound();

        return Results.Ok(await ToDetailAsync(conversation, principal, db, configuration, ct));
    }

    private static async Task<IResult> ListMessagesAsync(Guid id, int? page, int? pageSize, AppDbContext db, CancellationToken ct)
    {
        if (!await db.Conversations.AnyAsync(c => c.Id == id, ct))
            return Results.NotFound();

        var (resolvedPage, resolvedPageSize) = ResolvePaging(page, pageSize);
        // Internal notes are a staff-inbox thing and never exist on a мизоҷ channel; excluded anyway.
        var query = db.Messages.AsNoTracking().Where(m => m.ConversationId == id && !m.IsInternalNote);

        var totalCount = await query.CountAsync(ct);
        var messages = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((resolvedPage - 1) * resolvedPageSize)
            .Take(resolvedPageSize)
            .ToListAsync(ct);

        var items = messages.Select(m => MessageDto.FromEntity(m, MediaBase)).ToList();
        return Results.Ok(new PagedResult<MessageDto>(items, totalCount, resolvedPage, resolvedPageSize));
    }

    /// <summary>
    /// Same path as a staff reply: stored Pending at once, sent by WhatsAppSendJob (which also
    /// sends Instagram and Facebook) after the cancel window. Refused up front when it can't be
    /// sent (CannotSendAsync).
    /// </summary>
    private static async Task<IResult> SendAsync(
        Guid id,
        SendCustomerMessageRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        IBackgroundJobClient backgroundJobs,
        IConfiguration configuration,
        IInboxEventPublisher events,
        CancellationToken ct)
    {
        var conversation = await db.Conversations.Include(c => c.Channel).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (conversation is null)
            return Results.NotFound();

        var customerId = principal.GetUserId();
        if (await CannotSendAsync(conversation, customerId, db, configuration, ct) is { } refused)
            return refused;

        var senderName = await db.Customers.AsNoTracking()
            .Where(c => c.Id == customerId).Select(c => c.FullName).FirstAsync(ct);

        var message = new Message
        {
            Id = Guid.CreateVersion7(),
            ConversationId = conversation.Id,
            Direction = MessageDirection.Outbound,
            Type = MessageType.Text,
            Body = request.Body.Trim(),
            DeliveryStatus = MessageDeliveryStatus.Pending,
            // SentByUserId points at staff users — a мизоҷ is not one; the name snapshot is enough.
            SentByUserId = null,
            SentByUserName = senderName,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Messages.Add(message);
        await db.SaveChangesAsync(ct);

        var delaySeconds = configuration.GetValue("Inbox:DelayedSendSeconds", 45);
        backgroundJobs.Schedule<WhatsAppSendJob>(
            j => j.SendAsync(message.Id, CancellationToken.None), TimeSpan.FromSeconds(delaySeconds));

        var dto = MessageDto.FromEntity(message, MediaBase);
        await events.MessageSentAsync(conversation.ChannelId, assignedTo: null, dto, ct);

        return Results.Created($"/api/public/conversations/{id}/messages/{message.Id}", dto);
    }

    /// <summary>
    /// A photo, video, audio or PDF into the мизоҷ's own chat — the same checks as a text reply
    /// (their chat, a plan, a connected account, the 24-hour window), then MediaSendJob sends it
    /// exactly as the staff inbox does. The file is stored under a new name with the extension of
    /// its (allowed) type — never the name or path the browser gave.
    /// </summary>
    public static async Task<IResult> SendMediaAsync(
        Guid id,
        IFormFile file,
        ClaimsPrincipal principal,
        AppDbContext db,
        IBackgroundJobClient backgroundJobs,
        IConfiguration configuration,
        IWebHostEnvironment env,
        IInboxEventPublisher events,
        CancellationToken ct)
    {
        var conversation = await db.Conversations.Include(c => c.Channel).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (conversation is null)
            return Results.NotFound();

        var customerId = principal.GetUserId();
        if (await CannotSendAsync(conversation, customerId, db, configuration, ct) is { } refused)
            return refused;

        var mimeType = (file.ContentType ?? "").Split(';')[0].Trim().ToLowerInvariant();
        if (!SendableMediaTypes.TryGetValue(mimeType, out var extension) || !ChannelCapabilities.CanSendMedia(conversation.Channel.Type))
        {
            return Results.Problem(
                title: "Ин навъи файл фиристода намешавад",
                detail: "Сурат (JPG, PNG, GIF, WEBP, HEIC), видео (MP4, MOV), овоз (M4A, MP3) ё PDF фиристед.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var (messageType, maxSizeBytes) = MediaUploadValidator.Classify(conversation.Channel.Type, mimeType);
        if (!MediaUploadValidator.IsWithinLimit(conversation.Channel.Type, mimeType, file.Length))
        {
            return Results.Problem(
                title: "Файл калон аст",
                detail: $"Барои ин навъи файл ҳадди аксар {maxSizeBytes / (1024 * 1024)} МБ аст.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var originalName = Path.GetFileName(file.FileName ?? "");
        return await StoreAndQueueAsync(
            conversation, file, messageType, mimeType, extension, isVoiceNote: false,
            string.IsNullOrWhiteSpace(originalName) ? null : originalName.Length > 200 ? originalName[..200] : originalName,
            customerId, db, backgroundJobs, configuration, env, events, ct);
    }

    /// <summary>
    /// A voice note recorded in the мизоҷ's browser, into their own chat — the same checks as any
    /// reply (their chat, a plan, a connected account, the 24-hour window), then MediaSendJob
    /// makes it AAC and sends it exactly as a staff voice note. Only recording types are taken,
    /// stored under a new name.
    /// </summary>
    public static async Task<IResult> SendVoiceNoteAsync(
        Guid id,
        IFormFile file,
        ClaimsPrincipal principal,
        AppDbContext db,
        IBackgroundJobClient backgroundJobs,
        IConfiguration configuration,
        IWebHostEnvironment env,
        IInboxEventPublisher events,
        CancellationToken ct)
    {
        var conversation = await db.Conversations.Include(c => c.Channel).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (conversation is null)
            return Results.NotFound();

        var customerId = principal.GetUserId();
        if (await CannotSendAsync(conversation, customerId, db, configuration, ct) is { } refused)
            return refused;

        var mimeType = (file.ContentType ?? "").Split(';')[0].Trim().ToLowerInvariant();
        if (file.Length <= 0 || !VoiceNoteTypes.TryGetValue(mimeType, out var extension) ||
            !ChannelCapabilities.CanSendVoice(conversation.Channel.Type))
        {
            return Results.Problem(
                title: "Паёми овозӣ фиристода нашуд",
                detail: "Овоз сабт нашуд ё навъаш ношинос аст — аз нав сабт кунед.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!MediaUploadValidator.IsWithinLimit(conversation.Channel.Type, mimeType, file.Length))
        {
            var maxSizeBytes = MediaUploadValidator.Classify(conversation.Channel.Type, mimeType).MaxSizeBytes;
            return Results.Problem(
                title: "Паёми овозӣ дароз аст",
                detail: $"Паёми овозӣ то {maxSizeBytes / (1024 * 1024)} МБ бошад — кӯтоҳтар сабт кунед.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return await StoreAndQueueAsync(
            conversation, file, MessageType.Audio, mimeType, extension, isVoiceNote: true, originalFileName: null,
            customerId, db, backgroundJobs, configuration, env, events, ct);
    }

    /// <summary>
    /// Why the мизоҷ may not send into this chat now, or null when they may: the plan has run
    /// out, the account must be reconnected, or Meta's 24-hour window has closed — Instagram
    /// would refuse those anyway, only later and less clearly.
    /// </summary>
    private static async Task<IResult?> CannotSendAsync(
        Conversation conversation, Guid customerId, AppDbContext db, IConfiguration configuration, CancellationToken ct)
    {
        if (await CustomerEntitlements.LoadLimitsAsync(customerId, db, configuration, ct) is null)
            return CustomerEntitlements.NoAccessProblem();

        if (!conversation.Channel.IsActive || conversation.Channel.RequiresReconnect)
        {
            return Results.Problem(
                title: "Аккаунт пайваст нест",
                detail: "Instagram-ро дар бахши «Автоматизатсия» аз нав пайваст кунед, баъд ҷавоб диҳед.",
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?> { ["code"] = "reconnect" });
        }

        if (conversation.WindowExpiresAt is { } windowEnd && windowEnd <= DateTimeOffset.UtcNow)
        {
            return Results.Problem(
                title: "Вақти ҷавоб гузашт",
                detail: "Instagram ҷавобро танҳо то 24 соат баъд аз паёми охирини мухлис иҷозат медиҳад.",
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?> { ["code"] = "window_closed" });
        }

        return null;
    }

    /// <summary>The file under a new name in the channel's own folder, a Pending message, then MediaSendJob.</summary>
    private static async Task<IResult> StoreAndQueueAsync(
        Conversation conversation, IFormFile file, MessageType messageType, string mimeType, string extension,
        bool isVoiceNote, string? originalFileName, Guid customerId, AppDbContext db, IBackgroundJobClient backgroundJobs,
        IConfiguration configuration, IWebHostEnvironment env, IInboxEventPublisher events, CancellationToken ct)
    {
        var rootPath = UploadsPathResolver.ResolveRootPath(configuration, env);
        var relativePath = Path.Combine("whatsapp-media", conversation.ChannelId.ToString(), $"{Guid.CreateVersion7()}{extension}");
        var fullPath = Path.Combine(rootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using (var stream = File.Create(fullPath))
            await file.CopyToAsync(stream, ct);

        var senderName = await db.Customers.AsNoTracking()
            .Where(c => c.Id == customerId).Select(c => c.FullName).FirstAsync(ct);
        var message = new Message
        {
            Id = Guid.CreateVersion7(),
            ConversationId = conversation.Id,
            Direction = MessageDirection.Outbound,
            Type = messageType,
            MediaUrl = relativePath,
            MimeType = mimeType,
            SizeBytes = file.Length,
            OriginalFileName = originalFileName,
            DeliveryStatus = MessageDeliveryStatus.Pending,
            // SentByUserId points at staff users — a мизоҷ is not one; the name snapshot is enough.
            SentByUserId = null,
            SentByUserName = senderName,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Messages.Add(message);
        await db.SaveChangesAsync(ct);

        backgroundJobs.Enqueue<MediaSendJob>(j => j.SendAsync(message.Id, isVoiceNote, CancellationToken.None));

        var dto = MessageDto.FromEntity(message, MediaBase);
        await events.MessageSentAsync(conversation.ChannelId, assignedTo: null, dto, ct);

        return Results.Accepted($"/api/public/conversations/{conversation.Id}/messages/{message.Id}", dto);
    }

    /// <summary>
    /// Atomic, like the staff cancel: only a still-Pending message is cancelled, so this and
    /// WhatsAppSendJob can never both win. Needs a plan, like sending.
    /// </summary>
    private static async Task<IResult> CancelAsync(
        Guid id,
        Guid messageId,
        ClaimsPrincipal principal,
        AppDbContext db,
        IConfiguration configuration,
        IInboxEventPublisher events,
        CancellationToken ct)
    {
        var conversation = await db.Conversations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (conversation is null)
            return Results.NotFound();

        if (await CustomerEntitlements.LoadLimitsAsync(principal.GetUserId(), db, configuration, ct) is null)
            return CustomerEntitlements.NoAccessProblem();

        var message = await db.Messages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == messageId && m.ConversationId == id, ct);
        if (message is null)
            return Results.NotFound();

        var cancelled = await db.Messages
            .Where(m => m.Id == messageId && m.DeliveryStatus == MessageDeliveryStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.DeliveryStatus, MessageDeliveryStatus.Cancelled), ct);

        if (cancelled == 0)
        {
            return Results.Problem(
                title: "Бекор карда нашуд",
                detail: "Паём аллакай фиристода шудааст — дигар бекор кардан мумкин нест.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var dto = MessageDto.FromEntity(message, MediaBase) with { DeliveryStatus = MessageDeliveryStatus.Cancelled.ToString() };
        await events.MessageSentAsync(conversation.ChannelId, assignedTo: null, dto, ct);
        return Results.Ok(dto);
    }

    private static async Task<IResult> MarkAsReadAsync(
        Guid id,
        ClaimsPrincipal principal,
        AppDbContext db,
        IConfiguration configuration,
        IInboxEventPublisher events,
        CancellationToken ct)
    {
        var conversation = await db.Conversations.Include(c => c.Channel).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (conversation is null)
            return Results.NotFound();

        var inbound = await db.Messages
            .Where(m => m.ConversationId == id && m.Direction == MessageDirection.Inbound)
            .ToListAsync(ct);
        foreach (var message in inbound.Where(m => UnreadMessageSelector.ShouldMarkAsRead(m.Direction, m.DeliveryStatus)))
            message.DeliveryStatus = MessageDeliveryStatus.Read;

        conversation.UnreadCount = 0;
        await db.SaveChangesAsync(ct);

        var dto = await ToDetailAsync(conversation, principal, db, configuration, ct);
        await events.ConversationStatusChangedAsync(conversation.ChannelId, assignedTo: null, dto, ct);
        return Results.Ok(dto);
    }

    private static Task<IResult> DownloadMediaAsync(
        Guid messageId, AppDbContext db, IConfiguration configuration, IWebHostEnvironment env, HttpContext http, CancellationToken ct) =>
        ServeAsync(messageId, thumbnail: false, db, configuration, env, http, ct);

    private static Task<IResult> DownloadThumbnailAsync(
        Guid messageId, AppDbContext db, IConfiguration configuration, IWebHostEnvironment env, HttpContext http, CancellationToken ct) =>
        ServeAsync(messageId, thumbnail: true, db, configuration, env, http, ct);

    /// <summary>The staff media endpoint's rules (MediaAccessDecision), with the tenant filter as the access check.</summary>
    private static async Task<IResult> ServeAsync(
        Guid messageId, bool thumbnail, AppDbContext db, IConfiguration configuration, IWebHostEnvironment env,
        HttpContext http, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "private, no-store";
        http.Response.Headers.XContentTypeOptions = "nosniff";

        var message = await db.Messages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == messageId, ct);

        if (thumbnail)
        {
            var thumbOutcome = MediaAccessDecision.Evaluate(
                messageFound: message is not null, hasAccess: message is not null, message?.ThumbnailUrl, downloadError: null, deletedAt: null);
            return thumbOutcome == MediaAccessOutcome.Ready
                ? MessagesEndpoints.ServeStoredFileAsync(message!.ThumbnailUrl!, "image/jpeg", null, MessageType.Image, configuration, env)
                : Results.NotFound();
        }

        var outcome = MediaAccessDecision.Evaluate(
            messageFound: message is not null, hasAccess: message is not null,
            message?.MediaUrl, message?.MediaDownloadError, message?.MediaDeletedAt);

        return outcome switch
        {
            MediaAccessOutcome.DownloadFailed => MessagesEndpoints.DownloadFailedProblem(message!.MediaDownloadError!),
            MediaAccessOutcome.Deleted => MessagesEndpoints.DeletedProblem(message!.MediaDeletedAt!.Value),
            MediaAccessOutcome.Ready => MessagesEndpoints.ServeStoredFileAsync(
                message!.MediaUrl!, message.MimeType, message.OriginalFileName, message.Type, configuration, env),
            _ => Results.NotFound(),
        };
    }

    private static async Task<CustomerConversationDetail> ToDetailAsync(
        Conversation c, ClaimsPrincipal principal, AppDbContext db, IConfiguration configuration, CancellationToken ct)
    {
        var canSend = await CustomerEntitlements.LoadLimitsAsync(principal.GetUserId(), db, configuration, ct) is not null;
        return new CustomerConversationDetail(
            c.Id, c.ChannelId, c.Channel.Type.ToString(), c.Channel.Name,
            c.ContactName, c.ContactAvatarUrl, c.ContactUsername,
            c.LastMessageAt, c.UnreadCount, c.WindowExpiresAt, c.CreatedAt,
            canSend, ChannelNeedsReconnect: !c.Channel.IsActive || c.Channel.RequiresReconnect);
    }

    private static (int Page, int PageSize) ResolvePaging(int? page, int? pageSize) => (
        page is > 0 ? page.Value : 1,
        pageSize switch
        {
            null or <= 0 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => pageSize.Value,
        });
}
