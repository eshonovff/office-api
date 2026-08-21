using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Media;
using Permissions = Office.Api.Auth.Permissions;

namespace Office.Api.Features.Conversations;

/// <summary>
/// Пешниҳоди боэътимоди файли захирашуда — ҳеҷ гоҳ мустақим тавассути nginx/static
/// files, танҳо тавассути ин endpoint-ҳо (доступ тибқи ҳамон <see cref="IChannelAccessGuard"/>-и
/// Conversations, Cache-Control: private барои ҳуҷҷатҳои мижоз).
/// </summary>
public static class MessagesEndpoints
{
    public static IEndpointRouteBuilder MapMessagesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/messages").WithTags("Messages");

        group.MapGet("/{messageId:guid}/media", DownloadMediaAsync)
            .RequirePermission(Permissions.Inbox.View)
            .WithSummary("Боргирии файли медиа — расм/овоз/видео inline, ҳуҷҷат ҳамчун замима")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status410Gone)
            .ProducesProblem(StatusCodes.Status424FailedDependency);

        group.MapGet("/{messageId:guid}/thumbnail", DownloadThumbnailAsync)
            .RequirePermission(Permissions.Inbox.View)
            .WithSummary("Боргирии thumbnail-и расм")
            .Produces(StatusCodes.Status200OK, contentType: "image/jpeg")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status410Gone)
            .ProducesProblem(StatusCodes.Status424FailedDependency);

        return app;
    }

    private static async Task<IResult> DownloadMediaAsync(
        Guid messageId,
        ClaimsPrincipal principal,
        AppDbContext db,
        IChannelAccessGuard access,
        IConfiguration configuration,
        IWebHostEnvironment env,
        HttpContext httpContext,
        CancellationToken ct)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";

        var message = await LoadMessageAsync(messageId, db, ct);
        var hasAccess = message is not null && await access.HasAccessAsync(
            principal, message.Conversation.ChannelId, message.Conversation.AssignedTo, ct);

        var outcome = MediaAccessDecision.Evaluate(
            messageFound: message is not null, hasAccess, message?.MediaUrl, message?.MediaDownloadError, message?.MediaDeletedAt);

        return outcome switch
        {
            MediaAccessOutcome.DownloadFailed => DownloadFailedProblem(message!.MediaDownloadError!),
            MediaAccessOutcome.Deleted => DeletedProblem(message!.MediaDeletedAt!.Value),
            MediaAccessOutcome.Ready => ServeStoredFileAsync(
                message!.MediaUrl!, message.MimeType, message.OriginalFileName, message.Type, configuration, env),
            _ => Results.NotFound(),
        };
    }

    private static async Task<IResult> DownloadThumbnailAsync(
        Guid messageId,
        ClaimsPrincipal principal,
        AppDbContext db,
        IChannelAccessGuard access,
        IConfiguration configuration,
        IWebHostEnvironment env,
        HttpContext httpContext,
        CancellationToken ct)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";

        var message = await LoadMessageAsync(messageId, db, ct);
        var hasAccess = message is not null && await access.HasAccessAsync(
            principal, message.Conversation.ChannelId, message.Conversation.AssignedTo, ct);

        // downloadError/deletedAt intentionally NOT passed here (unlike DownloadMediaAsync):
        // both describe the state of the main media file, not the thumbnail. A thumbnail is
        // generated once, synchronously, only after the main download already succeeded — if
        // that download later failed there's no ThumbnailUrl to begin with (falls through to
        // NotFound below on its own) — and MediaRetentionCleanupJob deliberately never deletes
        // thumbnails when it removes the main file (see its own doc comment), specifically so
        // a still-image preview survives after the full video/file is gone. Passing the main
        // media's MediaDeletedAt here was reporting 410 Gone for a thumbnail file that was
        // still sitting right there on disk.
        var outcome = MediaAccessDecision.Evaluate(
            messageFound: message is not null, hasAccess, message?.ThumbnailUrl, downloadError: null, deletedAt: null);

        return outcome switch
        {
            MediaAccessOutcome.Ready => ServeStoredFileAsync(
                message!.ThumbnailUrl!, "image/jpeg", null, MessageType.Image, configuration, env),
            _ => Results.NotFound(),
        };
    }

    private static Task<Message?> LoadMessageAsync(Guid messageId, AppDbContext db, CancellationToken ct) =>
        db.Messages.AsNoTracking().Include(m => m.Conversation).FirstOrDefaultAsync(m => m.Id == messageId, ct);

    private static IResult ServeStoredFileAsync(
        string relativePath, string? mimeType, string? originalFileName, MessageType type,
        IConfiguration configuration, IWebHostEnvironment env)
    {
        var rootPath = UploadsPathResolver.ResolveRootPath(configuration, env);
        var fullPath = SafeUploadsPath.TryResolve(rootPath, relativePath);

        if (fullPath is null || !File.Exists(fullPath))
            return Results.NotFound();

        var isInline = type is MessageType.Image or MessageType.Audio or MessageType.Video;

        return Results.File(
            fullPath,
            mimeType ?? "application/octet-stream",
            fileDownloadName: isInline ? null : (originalFileName ?? Path.GetFileName(fullPath)),
            enableRangeProcessing: true);
    }

    private static IResult DownloadFailedProblem(string error) => Results.Problem(
        title: "Боркунии медиа ноком шуд",
        detail: error,
        statusCode: StatusCodes.Status424FailedDependency);

    private static IResult DeletedProblem(DateTimeOffset deletedAt) => Results.Problem(
        title: "Файл дигар дар сервер нест",
        detail: $"Ин файл тибқи мӯҳлати нигоҳдорӣ дар {deletedAt:yyyy-MM-dd} нест карда шудааст.",
        statusCode: StatusCodes.Status410Gone);
}
