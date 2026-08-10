using System.Security.Claims;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Channels;
using Office.Api.Channels.WhatsApp;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
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
            .WithSummary("Ҷавоб фиристодан — матни озод (тиреза кушода) ё шаблон (тиреза баста)")
            .Produces<MessageDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
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

        if (request.AssignedTo is not null)
        {
            var assigneeExists = await db.Users.AnyAsync(u => u.Id == request.AssignedTo, ct);
            if (!assigneeExists)
            {
                return Results.Problem(
                    title: "Корбари нодуруст",
                    detail: "Корманди таъиншуда вуҷуд надорад.",
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }

        var statusChanged = newStatus is not null && newStatus != conversation.Status;
        var assignmentChanged = request.AssignedTo is not null && request.AssignedTo != conversation.AssignedTo;

        if (newStatus is not null)
            conversation.Status = newStatus.Value;

        if (request.AssignedTo is not null)
            conversation.AssignedTo = request.AssignedTo;

        await db.SaveChangesAsync(ct);
        await db.Entry(conversation).Reference(c => c.Assignee).LoadAsync(ct);

        var dto = ToDetail(conversation);

        if (assignmentChanged)
            await events.ConversationAssignedAsync(conversation.ChannelId, conversation.AssignedTo, dto, ct);

        if (statusChanged)
            await events.ConversationStatusChangedAsync(conversation.ChannelId, conversation.AssignedTo, dto, ct);

        return Results.Ok(dto);
    }

    private static async Task<IResult> SendMessageAsync(
        Guid id,
        SendMessageRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        IChannelAccessGuard access,
        IChannelProviderFactory factory,
        IInboxEventPublisher events,
        CancellationToken ct)
    {
        var conversation = await db.Conversations.Include(c => c.Channel).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (conversation is null)
            return Results.NotFound();

        if (!await access.HasAccessAsync(principal, conversation.ChannelId, conversation.AssignedTo, ct))
            return Results.NotFound();

        var isTemplate = !string.IsNullOrEmpty(request.TemplateName);
        var userId = principal.GetUserId();

        var message = new Message
        {
            Id = Guid.CreateVersion7(),
            ConversationId = conversation.Id,
            Direction = MessageDirection.Outbound,
            Type = MessageType.Text,
            Body = isTemplate ? request.Body ?? $"[шаблон: {request.TemplateName}]" : request.Body,
            DeliveryStatus = MessageDeliveryStatus.Pending,
            SentByUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Messages.Add(message);
        await db.SaveChangesAsync(ct);

        var provider = factory.GetProvider(conversation.Channel.Type);

        try
        {
            if (isTemplate)
            {
                await provider.SendTemplateAsync(
                    conversation.Channel, conversation.ExternalId, request.TemplateName!,
                    request.TemplateLanguage ?? "en_US", request.TemplateParameters ?? [], ct);
            }
            else
            {
                await provider.SendMessageAsync(conversation.Channel, conversation.ExternalId, request.Body!, ct);
            }

            message.DeliveryStatus = MessageDeliveryStatus.Sent;
        }
        catch (WhatsAppWindowClosedException ex)
        {
            message.DeliveryStatus = MessageDeliveryStatus.Failed;
            await db.SaveChangesAsync(ct);

            return Results.Problem(
                title: "Тирезаи 24-соата баста аст",
                detail: ex.Message,
                statusCode: StatusCodes.Status409Conflict);
        }

        conversation.LastMessageAt = message.CreatedAt;
        await db.SaveChangesAsync(ct);

        var sender = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId, ct);
        var dto = ToMessageDto(message) with { SentByUserName = sender.FullName };
        await events.MessageSentAsync(conversation.ChannelId, conversation.AssignedTo, dto, ct);

        return Results.Created($"/api/conversations/{id}/messages/{message.Id}", dto);
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
        CancellationToken ct)
    {
        var conversation = await db.Conversations.Include(c => c.Channel).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (conversation is null)
            return Results.NotFound();

        if (!await access.HasAccessAsync(principal, conversation.ChannelId, conversation.AssignedTo, ct))
            return Results.NotFound();

        if (file.Length <= 0)
            return Results.BadRequest();

        var mimeType = string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType;
        var (messageType, maxSizeBytes) = MediaUploadValidator.Classify(mimeType);

        if (!MediaUploadValidator.IsWithinLimit(mimeType, file.Length))
            return SizeLimitProblem(maxSizeBytes);

        return await SaveAndEnqueueAsync(
            conversation, file, messageType, mimeType, isVoiceNote: false, forcedExtension: null,
            principal, db, backgroundJobs, configuration, env, ct);
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
        CancellationToken ct)
    {
        var conversation = await db.Conversations.Include(c => c.Channel).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (conversation is null)
            return Results.NotFound();

        if (!await access.HasAccessAsync(principal, conversation.ChannelId, conversation.AssignedTo, ct))
            return Results.NotFound();

        if (file.Length <= 0)
            return Results.BadRequest();

        // Ҳамеша аудио — MediaRecorder-и браузер webm/opus мефиристад, дар MediaSendJob ба ogg/opus transcode мешавад.
        if (!MediaUploadValidator.IsWithinLimit("audio/webm", file.Length))
            return SizeLimitProblem(MediaUploadValidator.AudioVideoMaxBytes);

        var mimeType = string.IsNullOrEmpty(file.ContentType) ? "audio/webm" : file.ContentType;

        return await SaveAndEnqueueAsync(
            conversation, file, MessageType.Audio, mimeType, isVoiceNote: true, forcedExtension: ".webm",
            principal, db, backgroundJobs, configuration, env, ct);
    }

    private static IResult SizeLimitProblem(long maxSizeBytes) => Results.Problem(
        title: "Файл калон аст",
        detail: $"Барои ин навъи файл ҳаҷми ҳадди аксар {maxSizeBytes / (1024 * 1024)} МБ аст.",
        statusCode: StatusCodes.Status400BadRequest);

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
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var rootPath = UploadsPathResolver.ResolveRootPath(configuration, env);
        var mediaFolder = Path.Combine(rootPath, "whatsapp-media", conversation.ChannelId.ToString());
        Directory.CreateDirectory(mediaFolder);

        var extension = forcedExtension ?? Path.GetExtension(file.FileName);
        var storedFileName = $"{Guid.CreateVersion7()}{extension}";
        var fullPath = Path.Combine(mediaFolder, storedFileName);

        await using (var stream = File.Create(fullPath))
            await file.CopyToAsync(stream, ct);

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
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Messages.Add(message);
        await db.SaveChangesAsync(ct);

        backgroundJobs.Enqueue<MediaSendJob>(j => j.SendAsync(message.Id, isVoiceNote, CancellationToken.None));

        var sender = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId, ct);
        var dto = ToMessageDto(message) with { SentByUserName = sender.FullName };

        return Results.Accepted($"/api/conversations/{conversation.Id}/messages/{message.Id}", dto);
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

        var query = db.Messages.AsNoTracking()
            .Include(m => m.SentByUser)
            .Where(m => m.ConversationId == id);

        var totalCount = await query.CountAsync(ct);
        var messages = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((resolvedPage - 1) * resolvedPageSize)
            .Take(resolvedPageSize)
            .ToListAsync(ct);

        var items = messages.Select(ToMessageDto).ToList();
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

    private static ConversationDetail ToDetail(Conversation c) => new(
        c.Id, c.ChannelId, c.Channel.Type.ToString(), c.Channel.Name, c.ExternalId,
        c.ContactName, c.ContactAvatarUrl, c.Status.ToString(), c.AssignedTo, c.Assignee?.FullName,
        c.LastMessageAt, c.UnreadCount, c.WindowExpiresAt, c.CreatedAt);

    private static MessageDto ToMessageDto(Message m) => new(
        m.Id, m.ConversationId, m.Direction.ToString(), m.Type.ToString(), m.Body,
        m.MediaUrl is not null ? $"/api/messages/{m.Id}/media" : null,
        m.ExternalId, m.DeliveryStatus.ToString(), m.IsInternalNote, m.SentByUserId, m.SentByUser?.FullName,
        m.CreatedAt, m.MimeType, m.SizeBytes, m.OriginalFileName, m.VoiceDurationSeconds,
        m.ThumbnailUrl is not null ? $"/api/messages/{m.Id}/thumbnail" : null,
        m.MediaDeletedAt, m.MediaDownloadError);
}
