using System.Security.Claims;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Channels;
using Office.Api.Channels.WhatsApp;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Features.Channels;
using Office.Api.Media;
using Office.Api.Realtime;
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

        group.MapGet("/{id:guid}/messages", ListMessagesAsync)
            .RequirePermission(Permissions.Inbox.View)
            .WithSummary("Таърихи паёмҳо — аз нав ба кӯҳна, саҳифабандӣшуда")
            .Produces<PagedResult<MessageDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/messages", SendMessageAsync)
            .WithValidation<SendMessageRequest>()
            .RequirePermission(Permissions.Inbox.Reply)
            .WithSummary(
                "Ҷавоб фиристодан — паём фавран Pending сабт мешавад ва бо таъхир (пешфарз 45с, " +
                "Inbox:DelayedSendSeconds) ба провайдер ирсол мешавад, то вақти бекор кардан бошад")
            .Produces<MessageDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/messages/{messageId:guid}/cancel", CancelMessageAsync)
            .RequirePermission(Permissions.Inbox.Reply)
            .WithSummary("Бекор кардани паёми Pending — пеш аз итмоми тирезаи таъхир. Баъд аз ирсол 409.")
            .Produces<MessageDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/{id:guid}", UpdateAsync)
            .WithValidation<UpdateConversationRequest>()
            .RequirePermission(Permissions.Inbox.Assign)
            .WithSummary("Иваз кардани статус ва/ё корманди таъиншуда — пӯшидан (`Closed`) бо inbox.close иловагӣ")
            .Produces<ConversationDetail>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/takeover", TakeoverAsync)
            .RequirePermission(Permissions.Inbox.Reply)
            .WithSummary("Гирифтани чат аз таъиншудаи қаблӣ — қасдан бе 24-соата lock, вале таърих сабт мешавад")
            .Produces<ConversationDetail>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/assignable-users", ListAssignableUsersAsync)
            .RequirePermission(Permissions.Inbox.Assign)
            .WithSummary("Корбароне, ки метавонанд ба ин чат таъин шаванд — узви канали ин чат + Owner/Admin")
            .Produces<IEnumerable<AssignableUserDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/assignment-history", ListAssignmentHistoryAsync)
            .RequirePermission(Permissions.Inbox.View)
            .WithSummary("Таърихи таъинот — аз нав ба кӯҳна, саҳифабандӣшуда (claim/takeover/reassign/auto-release)")
            .Produces<PagedResult<ConversationAssignmentEventDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/media", UploadMediaAsync)
            .DisableAntiforgery()
            .RequirePermission(Permissions.Inbox.Reply)
            .WithSummary("Фиристодани файли замима (расм/видео/овоз/ҳуҷҷат) — лимит вобаста ба навъ")
            .Produces<MessageDto>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/voice-note", UploadVoiceNoteAsync)
            .DisableAntiforgery()
            .RequirePermission(Permissions.Inbox.Reply)
            .WithSummary("Фиристодани voice note — webm/opus аз браузер, дар сервер ба ogg/opus transcode мешавад")
            .Produces<MessageDto>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/read", MarkAsReadAsync)
            .RequirePermission(Permissions.Inbox.View)
            .WithSummary("Хонда шуд гузоштан — паёмҳои воридотӣ Read, unreadCount = 0")
            .Produces<ConversationDetail>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> MarkAsReadAsync(
        Guid id,
        ClaimsPrincipal principal,
        AppDbContext db,
        IChannelAccessGuard access,
        IInboxEventPublisher events,
        CancellationToken ct)
    {
        var conversation = await db.Conversations
            .Include(c => c.Channel)
            .Include(c => c.Assignee)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (conversation is null)
            return Results.NotFound();

        if (!await access.HasAccessAsync(principal, conversation.ChannelId, conversation.AssignedTo, ct))
            return Results.NotFound();

        var inboundMessages = await db.Messages
            .Where(m => m.ConversationId == id && m.Direction == MessageDirection.Inbound)
            .ToListAsync(ct);

        foreach (var message in inboundMessages.Where(m => UnreadMessageSelector.ShouldMarkAsRead(m.Direction, m.DeliveryStatus)))
            message.DeliveryStatus = MessageDeliveryStatus.Read;

        conversation.UnreadCount = 0;
        await db.SaveChangesAsync(ct);

        var dto = ToDetail(conversation);

        // "Read" ба ин 4 event-и мавҷуда мустақим намеғунҷад — ConversationStatusChanged
        // ҳамчун сигнали умумии "ин чат нав шуд, аз нав кашед" истифода мешавад (payload
        // ҳамон ConversationDetail-и пурра аст, аз он unreadCount:0-ро фронтенд мебинад).
        await events.ConversationStatusChangedAsync(conversation.ChannelId, conversation.AssignedTo, dto, ct);

        return Results.Ok(dto);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateConversationRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        IChannelAccessGuard access,
        IInboxEventPublisher events,
        CancellationToken ct)
    {
        var conversation = await db.Conversations
            .Include(c => c.Channel)
            .Include(c => c.Assignee)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (conversation is null)
            return Results.NotFound();

        if (!await access.HasAccessAsync(principal, conversation.ChannelId, conversation.AssignedTo, ct))
            return Results.NotFound();

        ConversationStatus? newStatus = null;
        if (!string.IsNullOrEmpty(request.Status))
        {
            newStatus = Enum.Parse<ConversationStatus>(request.Status, ignoreCase: true);

            if (ConversationStatusChangeAuthorizer.RequiresClosePermission(newStatus.Value) &&
                !principal.HasPermission(Permissions.Inbox.Close))
            {
                return Results.Problem(
                    title: "Иҷозат нест",
                    detail: "Пӯшидани чат permission-и `inbox.close`-ро металабад.",
                    statusCode: StatusCodes.Status403Forbidden);
            }
        }

        User? newAssignee = null;
        if (request.AssignedTo is not null)
        {
            newAssignee = await db.Users.FirstOrDefaultAsync(u => u.Id == request.AssignedTo, ct);
            if (newAssignee is null)
            {
                return Results.Problem(
                    title: "Корбари нодуруст",
                    detail: "Корманди таъиншуда вуҷуд надорад.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (!await access.CanUserBeAssignedToChannelAsync(request.AssignedTo.Value, conversation.ChannelId, ct))
            {
                return Results.Problem(
                    title: "Корманд ба канал дастрасӣ надорад",
                    detail: "Корманди таъиншуда узви канали ин чат нест — баъд аз таъин чатро намебинад.",
                    statusCode: StatusCodes.Status409Conflict);
            }
        }

        var statusChanged = newStatus is not null && newStatus != conversation.Status;
        var assignmentChanged = request.AssignedTo is not null && request.AssignedTo != conversation.AssignedTo;
        var previousAssigneeId = conversation.AssignedTo;
        var previousAssigneeName = conversation.Assignee?.FullName;

        if (newStatus is not null)
            conversation.Status = newStatus.Value;

        if (request.AssignedTo is not null)
        {
            conversation.AssignedTo = request.AssignedTo;
            conversation.Assignee = newAssignee;
        }

        if (assignmentChanged)
        {
            RecordAssignmentHistory(
                db, conversation.Id, previousAssigneeId, previousAssigneeName,
                request.AssignedTo, newAssignee!.FullName, ConversationAssignmentReason.Reassigned);
        }

        await db.SaveChangesAsync(ct);

        var dto = ToDetail(conversation);

        if (assignmentChanged)
            await events.ConversationAssignedAsync(conversation.ChannelId, conversation.AssignedTo, dto, ct);

        if (statusChanged)
            await events.ConversationStatusChangedAsync(conversation.ChannelId, conversation.AssignedTo, dto, ct);

        return Results.Ok(dto);
    }

    /// <summary>
    /// Гирифтани чат аз таъиншудаи қаблӣ (агар бошад). Қасдан бе 24-соата lock: агар
    /// таъиншуда бемор шавад ё дастрас набошад, мижоз набояд бе ҷавоб монад — ҳама узви
    /// канал (на танҳо Owner/Admin/Manager бо inbox.assign) метавонанд ин кор кунанд, пас
    /// permission-и он ҳамон inbox.reply аст, на inbox.assign.
    /// </summary>
    private static async Task<IResult> TakeoverAsync(
        Guid id,
        ClaimsPrincipal principal,
        AppDbContext db,
        IChannelAccessGuard access,
        IInboxEventPublisher events,
        CancellationToken ct)
    {
        var conversation = await db.Conversations
            .Include(c => c.Channel)
            .Include(c => c.Assignee)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (conversation is null)
            return Results.NotFound();

        if (!await access.HasAccessAsync(principal, conversation.ChannelId, conversation.AssignedTo, ct))
            return Results.NotFound();

        var userId = principal.GetUserId();
        if (conversation.AssignedTo == userId)
            return Results.Ok(ToDetail(conversation));

        var previousAssigneeId = conversation.AssignedTo;
        var previousAssigneeName = conversation.Assignee?.FullName;

        var caller = await db.Users.FirstAsync(u => u.Id == userId, ct);
        conversation.AssignedTo = userId;
        conversation.Assignee = caller;

        RecordAssignmentHistory(
            db, conversation.Id, previousAssigneeId, previousAssigneeName,
            userId, caller.FullName, ConversationAssignmentReason.Takeover);

        await db.SaveChangesAsync(ct);

        var dto = ToDetail(conversation);
        await events.ConversationAssignedAsync(conversation.ChannelId, conversation.AssignedTo, dto, ct);

        return Results.Ok(dto);
    }

    /// <summary>
    /// Паём фавран Pending сабт мешавад, баъд бо таъхир (пешфарз 45с, Inbox:DelayedSendSeconds)
    /// тавассути WhatsAppSendJob ирсол мешавад — на дигар синхронӣ дар дохили ин handler.
    /// Ин ба operator фурсат медиҳад, ки хатогиро пеш аз расидан ба Meta бекор кунад (item 5) —
    /// WhatsApp на edit дорад, на delete баъд аз он ки паём ба Meta расид.
    /// </summary>
    private static async Task<IResult> SendMessageAsync(
        Guid id,
        SendMessageRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        IChannelAccessGuard access,
        IBackgroundJobClient backgroundJobs,
        IConfiguration configuration,
        IInboxEventPublisher events,
        CancellationToken ct)
    {
        var conversation = await db.Conversations.Include(c => c.Channel).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (conversation is null)
            return Results.NotFound();

        if (!await access.HasAccessAsync(principal, conversation.ChannelId, conversation.AssignedTo, ct))
            return Results.NotFound();

        var userId = principal.GetUserId();
        var isOwnerOrAdmin = ChannelAccessGuard.CanSeeAllChannels(principal);
        if (!ConversationAssignmentPolicy.CanSend(isOwnerOrAdmin, conversation.AssignedTo, userId))
            return ReadOnlyProblem();

        var isInternalNote = request.IsInternalNote;
        var isTemplate = !isInternalNote && !string.IsNullOrEmpty(request.TemplateName);
        // Tracked (not AsNoTracking) — assigned to conversation.Assignee below when claiming,
        // and EF needs it tracked to recognize that as the same entity rather than a new insert.
        var sender = await db.Users.FirstAsync(u => u.Id == userId, ct);

        // Claim on first reply — even if the send itself later fails (e.g. the 24h window
        // closes during the delay), the operator has already started handling this customer,
        // so the claim still sticks. Published before scheduling the dispatch, not after, so
        // other clients see the assignment immediately rather than 45s later. A note never
        // claims (ShouldClaimOnReply returns false for it) — it doesn't reach the customer,
        // so it isn't a "reply".
        var claimed = ConversationAssignmentPolicy.ShouldClaimOnReply(conversation.AssignedTo, isInternalNote);
        if (claimed)
        {
            conversation.AssignedTo = userId;
            conversation.Assignee = sender;
            RecordAssignmentHistory(
                db, conversation.Id, fromUserId: null, fromUserName: null,
                userId, sender.FullName, ConversationAssignmentReason.ClaimedOnReply);
        }

        var message = new Message
        {
            Id = Guid.CreateVersion7(),
            ConversationId = conversation.Id,
            Direction = MessageDirection.Outbound,
            Type = MessageType.Text,
            Body = isTemplate ? request.Body ?? $"[шаблон: {request.TemplateName}]" : request.Body,
            // Note: never Pending — it never gets scheduled for dispatch (see below), so
            // there's nothing pending about it. Sent from the moment it's stored; the thread
            // is its only real destination.
            DeliveryStatus = isInternalNote ? MessageDeliveryStatus.Sent : MessageDeliveryStatus.Pending,
            IsInternalNote = isInternalNote,
            SentByUserId = userId,
            SentByUserName = sender.FullName,
            TemplateName = isTemplate ? request.TemplateName : null,
            TemplateLanguage = isTemplate ? request.TemplateLanguage ?? "en_US" : null,
            TemplateParametersJson = isTemplate && request.TemplateParameters is { Count: > 0 }
                ? JsonSerializer.Serialize(request.TemplateParameters)
                : null,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Messages.Add(message);
        await db.SaveChangesAsync(ct);

        if (claimed)
            await events.ConversationAssignedAsync(conversation.ChannelId, conversation.AssignedTo, ToDetail(conversation), ct);

        // A note is never enqueued for dispatch — this, together with InternalNoteGuard's
        // hard check inside WhatsAppSendJob itself, is what makes it impossible for a note
        // to reach the provider: it's not just skipped here, it's refused there too.
        if (!isInternalNote)
        {
            var delaySeconds = configuration.GetValue("Inbox:DelayedSendSeconds", 45);
            backgroundJobs.Schedule<WhatsAppSendJob>(
                j => j.SendAsync(message.Id, CancellationToken.None), TimeSpan.FromSeconds(delaySeconds));
        }

        var dto = MessageDto.FromEntity(message);
        await events.MessageSentAsync(conversation.ChannelId, conversation.AssignedTo, dto, ct);

        return Results.Created($"/api/conversations/{id}/messages/{message.Id}", dto);
    }

    /// <summary>
    /// Атомӣ: танҳо агар паём то ҳол Pending бошад бекор мешавад. Race бо WhatsAppSendJob
    /// (агар таъхир аллакай гузашта бошад) бо ҳамин ExecuteUpdateAsync-и шартӣ ҳал мешавад —
    /// ҳарду тараф (ин ва job) ҳамин шарти "то ҳол Pending" - ро санҷанд, пас баробар
    /// расиданашон мумкин нест ки ҳарду муваффақ шаванд.
    /// </summary>
    private static async Task<IResult> CancelMessageAsync(
        Guid id,
        Guid messageId,
        ClaimsPrincipal principal,
        AppDbContext db,
        IChannelAccessGuard access,
        IInboxEventPublisher events,
        CancellationToken ct)
    {
        var conversation = await db.Conversations.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (conversation is null)
            return Results.NotFound();

        if (!await access.HasAccessAsync(principal, conversation.ChannelId, conversation.AssignedTo, ct))
            return Results.NotFound();

        var userId = principal.GetUserId();
        var isOwnerOrAdmin = ChannelAccessGuard.CanSeeAllChannels(principal);
        if (!ConversationAssignmentPolicy.CanSend(isOwnerOrAdmin, conversation.AssignedTo, userId))
            return ReadOnlyProblem();

        var message = await db.Messages.FirstOrDefaultAsync(m => m.Id == messageId && m.ConversationId == id, ct);
        if (message is null)
            return Results.NotFound();

        var cancelledCount = await db.Messages
            .Where(m => m.Id == messageId && m.DeliveryStatus == MessageDeliveryStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.DeliveryStatus, MessageDeliveryStatus.Cancelled), ct);

        if (cancelledCount == 0)
        {
            return Results.Problem(
                title: "Бекор карда нашуд",
                detail: "Паём аллакай ба провайдер фиристода шудааст (ё аллакай бекор шудааст) — дигар бекор кардан имконнопазир аст.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var dto = MessageDto.FromEntity(message) with { DeliveryStatus = MessageDeliveryStatus.Cancelled.ToString() };
        await events.MessageSentAsync(conversation.ChannelId, conversation.AssignedTo, dto, ct);

        return Results.Ok(dto);
    }

    private static async Task<IResult> UploadMediaAsync(
        Guid id,
        IFormFile file,
        ClaimsPrincipal principal,
        AppDbContext db,
        IChannelAccessGuard access,
        IBackgroundJobClient backgroundJobs,
        IConfiguration configuration,
        IWebHostEnvironment env,
        IInboxEventPublisher events,
        CancellationToken ct)
    {
        var conversation = await db.Conversations.Include(c => c.Channel).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (conversation is null)
            return Results.NotFound();

        if (!await access.HasAccessAsync(principal, conversation.ChannelId, conversation.AssignedTo, ct))
            return Results.NotFound();

        if (!ConversationAssignmentPolicy.CanSend(ChannelAccessGuard.CanSeeAllChannels(principal), conversation.AssignedTo, principal.GetUserId()))
            return ReadOnlyProblem();

        if (file.Length <= 0)
            return Results.BadRequest();

        var mimeType = string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType;
        var (messageType, maxSizeBytes) = MediaUploadValidator.Classify(mimeType);

        if (!MediaUploadValidator.IsWithinLimit(mimeType, file.Length))
            return SizeLimitProblem(maxSizeBytes);

        return await SaveAndEnqueueAsync(
            conversation, file, messageType, mimeType, isVoiceNote: false, forcedExtension: null,
            principal, db, backgroundJobs, configuration, env, events, ct);
    }

    private static async Task<IResult> UploadVoiceNoteAsync(
        Guid id,
        IFormFile file,
        ClaimsPrincipal principal,
        AppDbContext db,
        IChannelAccessGuard access,
        IBackgroundJobClient backgroundJobs,
        IConfiguration configuration,
        IWebHostEnvironment env,
        IInboxEventPublisher events,
        CancellationToken ct)
    {
        var conversation = await db.Conversations.Include(c => c.Channel).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (conversation is null)
            return Results.NotFound();

        if (!await access.HasAccessAsync(principal, conversation.ChannelId, conversation.AssignedTo, ct))
            return Results.NotFound();

        if (!ConversationAssignmentPolicy.CanSend(ChannelAccessGuard.CanSeeAllChannels(principal), conversation.AssignedTo, principal.GetUserId()))
            return ReadOnlyProblem();

        if (file.Length <= 0)
            return Results.BadRequest();

        // Ҳамеша аудио — MediaRecorder-и браузер webm/opus мефиристад, дар MediaSendJob ба ogg/opus transcode мешавад.
        if (!MediaUploadValidator.IsWithinLimit("audio/webm", file.Length))
            return SizeLimitProblem(MediaUploadValidator.AudioVideoMaxBytes);

        var mimeType = string.IsNullOrEmpty(file.ContentType) ? "audio/webm" : file.ContentType;

        return await SaveAndEnqueueAsync(
            conversation, file, MessageType.Audio, mimeType, isVoiceNote: true, forcedExtension: ".webm",
            principal, db, backgroundJobs, configuration, env, events, ct);
    }

    private static IResult SizeLimitProblem(long maxSizeBytes) => Results.Problem(
        title: "Файл калон аст",
        detail: $"Барои ин навъи файл ҳаҷми ҳадди аксар {maxSizeBytes / (1024 * 1024)} МБ аст.",
        statusCode: StatusCodes.Status400BadRequest);

    private static IResult ReadOnlyProblem() => Results.Problem(
        title: "Чат ба дигар корманд таъин шудааст",
        detail: "Шумо метавонед ин чатро бинед, вале барои фиристодан бояд аввал онро «гирифтан» (takeover) кунед.",
        statusCode: StatusCodes.Status403Forbidden);

    private static async Task<IResult> SaveAndEnqueueAsync(
        Conversation conversation,
        IFormFile file,
        MessageType messageType,
        string mimeType,
        bool isVoiceNote,
        string? forcedExtension,
        ClaimsPrincipal principal,
        AppDbContext db,
        IBackgroundJobClient backgroundJobs,
        IConfiguration configuration,
        IWebHostEnvironment env,
        IInboxEventPublisher events,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        // Tracked (not AsNoTracking) — assigned to conversation.Assignee below when claiming.
        var sender = await db.Users.FirstAsync(u => u.Id == userId, ct);
        var rootPath = UploadsPathResolver.ResolveRootPath(configuration, env);
        var mediaFolder = Path.Combine(rootPath, "whatsapp-media", conversation.ChannelId.ToString());
        Directory.CreateDirectory(mediaFolder);

        var extension = forcedExtension ?? Path.GetExtension(file.FileName);
        var storedFileName = $"{Guid.CreateVersion7()}{extension}";
        var fullPath = Path.Combine(mediaFolder, storedFileName);

        await using (var stream = File.Create(fullPath))
            await file.CopyToAsync(stream, ct);

        // Claim on first reply — same rule as the text-send path. Media/voice-note sends are
        // never internal notes (that's text-only, see SendMessageAsync).
        var claimed = ConversationAssignmentPolicy.ShouldClaimOnReply(conversation.AssignedTo, isInternalNote: false);
        if (claimed)
        {
            conversation.AssignedTo = userId;
            conversation.Assignee = sender;
            RecordAssignmentHistory(
                db, conversation.Id, fromUserId: null, fromUserName: null,
                userId, sender.FullName, ConversationAssignmentReason.ClaimedOnReply);
        }

        var message = new Message
        {
            Id = Guid.CreateVersion7(),
            ConversationId = conversation.Id,
            Direction = MessageDirection.Outbound,
            Type = messageType,
            MediaUrl = Path.Combine("whatsapp-media", conversation.ChannelId.ToString(), storedFileName),
            MimeType = mimeType,
            SizeBytes = file.Length,
            OriginalFileName = file.FileName,
            DeliveryStatus = MessageDeliveryStatus.Pending,
            SentByUserId = userId,
            SentByUserName = sender.FullName,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Messages.Add(message);
        await db.SaveChangesAsync(ct);

        if (claimed)
            await events.ConversationAssignedAsync(conversation.ChannelId, conversation.AssignedTo, ToDetail(conversation), ct);

        backgroundJobs.Enqueue<MediaSendJob>(j => j.SendAsync(message.Id, isVoiceNote, CancellationToken.None));

        var dto = MessageDto.FromEntity(message);

        return Results.Accepted($"/api/conversations/{conversation.Id}/messages/{message.Id}", dto);
    }

    private static async Task<IResult> ListAssignableUsersAsync(
        Guid id, ClaimsPrincipal principal, AppDbContext db, IChannelAccessGuard access, CancellationToken ct)
    {
        var conversation = await db.Conversations.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new { c.ChannelId, c.AssignedTo })
            .FirstOrDefaultAsync(ct);

        if (conversation is null)
            return Results.NotFound();

        if (!await access.HasAccessAsync(principal, conversation.ChannelId, conversation.AssignedTo, ct))
            return Results.NotFound();

        var users = await access.ApplyAssignableUsersFilter(db.Users.AsNoTracking(), conversation.ChannelId)
            .OrderBy(u => u.FullName)
            .Select(u => new AssignableUserDto(u.Id, u.FullName, u.Username))
            .ToListAsync(ct);

        return Results.Ok(users);
    }

    private static async Task<IResult> ListAssignmentHistoryAsync(
        Guid id, int? page, int? pageSize, ClaimsPrincipal principal, AppDbContext db, IChannelAccessGuard access, CancellationToken ct)
    {
        var conversation = await db.Conversations.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new { c.ChannelId, c.AssignedTo })
            .FirstOrDefaultAsync(ct);

        if (conversation is null)
            return Results.NotFound();

        if (!await access.HasAccessAsync(principal, conversation.ChannelId, conversation.AssignedTo, ct))
            return Results.NotFound();

        var (resolvedPage, resolvedPageSize) = ResolvePaging(page, pageSize);

        // FromUserName/ToUserName snapshot дар худи ҷадвал — Include(FromUser/ToUser) лозим нест.
        var query = db.ConversationAssignmentEvents.AsNoTracking().Where(e => e.ConversationId == id);

        var totalCount = await query.CountAsync(ct);
        var events = await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip((resolvedPage - 1) * resolvedPageSize)
            .Take(resolvedPageSize)
            .ToListAsync(ct);

        var items = events.Select(ConversationAssignmentEventDto.FromEntity).ToList();
        return Results.Ok(new PagedResult<ConversationAssignmentEventDto>(items, totalCount, resolvedPage, resolvedPageSize));
    }

    private static async Task<IResult> ListMessagesAsync(
        Guid id, int? page, int? pageSize, ClaimsPrincipal principal, AppDbContext db, IChannelAccessGuard access, CancellationToken ct)
    {
        var conversation = await db.Conversations.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new { c.ChannelId, c.AssignedTo })
            .FirstOrDefaultAsync(ct);

        if (conversation is null)
            return Results.NotFound();

        if (!await access.HasAccessAsync(principal, conversation.ChannelId, conversation.AssignedTo, ct))
            return Results.NotFound();

        var (resolvedPage, resolvedPageSize) = ResolvePaging(page, pageSize);

        // SentByUserName is a persisted snapshot on Message itself now — no need to
        // Include(SentByUser) just to resolve the display name.
        var query = db.Messages.AsNoTracking().Where(m => m.ConversationId == id);

        var totalCount = await query.CountAsync(ct);
        var messages = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((resolvedPage - 1) * resolvedPageSize)
            .Take(resolvedPageSize)
            .ToListAsync(ct);

        var items = messages.Select(MessageDto.FromEntity).ToList();
        return Results.Ok(new PagedResult<MessageDto>(items, totalCount, resolvedPage, resolvedPageSize));
    }

    private static async Task<IResult> ListAsync(
        Guid? channelId,
        string? status,
        Guid? assignedUserId,
        int? page,
        int? pageSize,
        ClaimsPrincipal principal,
        AppDbContext db,
        IChannelAccessGuard access,
        CancellationToken ct)
    {
        var query = db.Conversations.AsNoTracking()
            .Include(c => c.Channel)
            .Include(c => c.Assignee)
            .AsQueryable();

        query = await access.ApplyAccessFilterAsync(query, principal, ct);

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

    private static async Task<IResult> GetAsync(
        Guid id, ClaimsPrincipal principal, AppDbContext db, IChannelAccessGuard access, CancellationToken ct)
    {
        var conversation = await db.Conversations.AsNoTracking()
            .Include(c => c.Channel)
            .Include(c => c.Assignee)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (conversation is null)
            return Results.NotFound();

        if (!await access.HasAccessAsync(principal, conversation.ChannelId, conversation.AssignedTo, ct))
            return Results.NotFound();

        return Results.Ok(ToDetail(conversation));
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

    /// <summary>
    /// Internal, на private — ConversationAutoReleaseJob ҳам ҳамин mapping-ро истифода
    /// мебарад, то REST ва он чи job публикатсия мекунад ҳеҷ гоҳ дур нашаванд.
    /// </summary>
    internal static ConversationDetail ToDetail(Conversation c) => new(
        c.Id, c.ChannelId, c.Channel.Type.ToString(), c.Channel.Name, c.ExternalId,
        c.ContactName, c.ContactAvatarUrl, c.Status.ToString(), c.AssignedTo, c.Assignee?.FullName,
        c.LastMessageAt, c.UnreadCount, c.WindowExpiresAt, c.CreatedAt);

    /// <summary>
    /// Не save мекунад — дар SaveChangesAsync-и навбатии caller якҷоя мешавад. Номҳо
    /// snapshot (на танҳо FK), ҳамон сабабе, ки Message.SentByUserName-ро водор кард.
    /// </summary>
    private static void RecordAssignmentHistory(
        AppDbContext db,
        Guid conversationId,
        Guid? fromUserId,
        string? fromUserName,
        Guid? toUserId,
        string? toUserName,
        ConversationAssignmentReason reason) =>
        db.ConversationAssignmentEvents.Add(new ConversationAssignmentEvent
        {
            Id = Guid.CreateVersion7(),
            ConversationId = conversationId,
            FromUserId = fromUserId,
            FromUserName = fromUserName,
            ToUserId = toUserId,
            ToUserName = toUserName,
            Reason = reason,
            CreatedAt = DateTimeOffset.UtcNow,
        });
}
