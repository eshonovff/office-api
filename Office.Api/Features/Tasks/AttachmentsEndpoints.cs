using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Permissions = Office.Api.Auth.Permissions;

namespace Office.Api.Features.Tasks;

public static class AttachmentsEndpoints
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".txt", ".csv", ".zip",
    };

    public static IEndpointRouteBuilder MapAttachmentsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tasks/{taskId:guid}/attachments").WithTags("Tasks");

        group.MapGet("/", ListAsync).RequirePermission(Permissions.Tasks.View);
        group.MapPost("/", UploadAsync).RequirePermission(Permissions.Tasks.Edit).DisableAntiforgery();
        group.MapGet("/{attachmentId:guid}/download", DownloadAsync).RequirePermission(Permissions.Tasks.View);
        group.MapDelete("/{attachmentId:guid}", DeleteAsync).RequirePermission(Permissions.Tasks.Edit);

        return app;
    }

    private static async Task<IResult> ListAsync(
        Guid taskId, ClaimsPrincipal principal, AppDbContext db, ProjectAccessGuard access, CancellationToken ct)
    {
        var task = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null || !await access.HasAccessAsync(principal, task.ProjectId, ct))
            return Results.NotFound();

        var attachments = await db.TaskAttachments.AsNoTracking()
            .Where(a => a.TaskId == taskId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

        return Results.Ok(attachments.Select(a => new TaskAttachmentDto(a.Id, a.FileName, a.SizeBytes, a.UploadedBy, a.CreatedAt)));
    }

    private static async Task<IResult> UploadAsync(
        Guid taskId,
        IFormFile file,
        ClaimsPrincipal principal,
        AppDbContext db,
        ProjectAccessGuard access,
        IConfiguration configuration,
        IWebHostEnvironment env,
        CancellationToken ct)
    {
        var task = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null || !await access.HasAccessAsync(principal, task.ProjectId, ct))
            return Results.NotFound();

        if (file.Length <= 0)
            return Results.BadRequest();

        var maxSizeBytes = configuration.GetValue<long?>("Uploads:MaxSizeBytes") ?? 20 * 1024 * 1024;
        if (file.Length > maxSizeBytes)
        {
            return Results.Problem(
                title: "Файл калон аст",
                detail: $"Ҳаҷми файл набояд аз {maxSizeBytes / (1024 * 1024)} МБ зиёд бошад.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
        {
            return Results.Problem(
                title: "Навъи файл иҷозат дода нашудааст",
                detail: "Ин навъи файлро бор кардан мумкин нест.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var rootPath = ResolveRootPath(configuration, env);
        var taskFolder = Path.Combine(rootPath, taskId.ToString());
        Directory.CreateDirectory(taskFolder);

        var storedFileName = $"{Guid.CreateVersion7()}{extension}";
        var fullPath = Path.Combine(taskFolder, storedFileName);

        await using (var stream = File.Create(fullPath))
            await file.CopyToAsync(stream, ct);

        var userId = principal.GetUserId();
        var attachment = new TaskAttachment
        {
            Id = Guid.CreateVersion7(),
            TaskId = taskId,
            FileName = file.FileName,
            FilePath = Path.Combine(taskId.ToString(), storedFileName),
            SizeBytes = file.Length,
            UploadedBy = userId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.TaskAttachments.Add(attachment);
        db.TaskActivities.Add(new TaskActivity
        {
            Id = Guid.CreateVersion7(),
            TaskId = taskId,
            UserId = userId,
            Action = "attachment_added",
            PayloadJson = JsonSerializer.Serialize(new { attachment.FileName }),
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(ct);

        return Results.Created(
            $"/api/tasks/{taskId}/attachments/{attachment.Id}",
            new TaskAttachmentDto(attachment.Id, attachment.FileName, attachment.SizeBytes, attachment.UploadedBy, attachment.CreatedAt));
    }

    private static async Task<IResult> DownloadAsync(
        Guid taskId,
        Guid attachmentId,
        ClaimsPrincipal principal,
        AppDbContext db,
        ProjectAccessGuard access,
        IConfiguration configuration,
        IWebHostEnvironment env,
        CancellationToken ct)
    {
        var task = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null || !await access.HasAccessAsync(principal, task.ProjectId, ct))
            return Results.NotFound();

        var attachment = await db.TaskAttachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == attachmentId && a.TaskId == taskId, ct);
        if (attachment is null)
            return Results.NotFound();

        var rootPath = ResolveRootPath(configuration, env);
        var fullPath = Path.Combine(rootPath, attachment.FilePath);
        if (!File.Exists(fullPath))
            return Results.NotFound();

        return Results.File(fullPath, "application/octet-stream", attachment.FileName);
    }

    private static async Task<IResult> DeleteAsync(
        Guid taskId,
        Guid attachmentId,
        ClaimsPrincipal principal,
        AppDbContext db,
        ProjectAccessGuard access,
        IConfiguration configuration,
        IWebHostEnvironment env,
        CancellationToken ct)
    {
        var task = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null || !await access.HasAccessAsync(principal, task.ProjectId, ct))
            return Results.NotFound();

        var attachment = await db.TaskAttachments.FirstOrDefaultAsync(a => a.Id == attachmentId && a.TaskId == taskId, ct);
        if (attachment is null)
            return Results.NotFound();

        var rootPath = ResolveRootPath(configuration, env);
        var fullPath = Path.Combine(rootPath, attachment.FilePath);
        if (File.Exists(fullPath))
            File.Delete(fullPath);

        db.TaskAttachments.Remove(attachment);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static string ResolveRootPath(IConfiguration configuration, IWebHostEnvironment env)
    {
        var configured = configuration["Uploads:RootPath"];
        var basePath = configured is { Length: > 0 }
            ? (Path.IsPathRooted(configured) ? configured : Path.Combine(env.ContentRootPath, configured))
            : Path.Combine(env.ContentRootPath, "uploads");

        return Path.GetFullPath(basePath);
    }
}
