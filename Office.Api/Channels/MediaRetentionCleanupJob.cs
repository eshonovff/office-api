using Microsoft.EntityFrameworkCore;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Media;

namespace Office.Api.Channels;

/// <summary>
/// Тозакунии худкори медиаи кӯҳна (Hangfire recurring job). Файлро нест мекунад
/// ва <see cref="Data.Entities.Message.MediaDeletedAt"/>-ро мегузорад — сатри
/// паём мемонад, то UI гуфта тавонад файл дигар дар сервер нест. Thumbnail
/// ҳеҷ гоҳ дар ин ҷо нест намешавад.
/// </summary>
public class MediaRetentionCleanupJob(
    AppDbContext db,
    IConfiguration configuration,
    IWebHostEnvironment env,
    ILogger<MediaRetentionCleanupJob> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var options = new MediaRetentionOptions(
            ImageDays: configuration.GetValue("MediaRetention:ImageDays", 365),
            VoiceDays: configuration.GetValue("MediaRetention:VoiceDays", 365),
            DocumentDays: configuration.GetValue("MediaRetention:DocumentDays", 365),
            VideoDays: configuration.GetValue("MediaRetention:VideoDays", 7));

        var rootPath = UploadsPathResolver.ResolveRootPath(configuration, env);
        var now = DateTimeOffset.UtcNow;

        var candidates = await db.Messages
            .Where(m => m.MediaUrl != null && m.MediaDeletedAt == null)
            .ToListAsync(ct);

        var deletedCount = 0;
        foreach (var message in candidates)
        {
            if (!MediaRetentionPolicy.IsExpired(message.Type, message.CreatedAt, now, options))
                continue;

            var fullPath = Path.Combine(rootPath, message.MediaUrl!);
            if (File.Exists(fullPath))
                File.Delete(fullPath);

            message.MediaDeletedAt = now;
            deletedCount++;
        }

        if (deletedCount == 0)
            return;

        await db.SaveChangesAsync(ct);
        logger.LogInformation("MediaRetentionCleanupJob: {Count} файли медиа нест карда шуд.", deletedCount);
    }
}
