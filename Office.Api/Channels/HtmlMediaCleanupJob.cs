using Hangfire;
using Microsoft.EntityFrameworkCore;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Media;

namespace Office.Api.Channels;

/// <summary>
/// Тозакунии файлҳое, ки ПЕШ аз илова шудани MediaContentTypeValidator ба MediaDownloadJob
/// ҳамчун "муваффақ" сабт шуда буданд, вале воқеан саҳифаи HTML-и хатогии Meta CDN буданд
/// (URL-и мӯҳлаташгузашта — на 401/404, балки 200+HTML). Зеркашии дубора фоида надорад: URL-ҳо
/// аллакай мӯҳлаташон гузаштааст — файлҳо абадан гум шудаанд. Recurring, вале пас аз тозакунии
/// якум ҳамеша холӣ бармегардонад (арзон барои иҷрои такрорӣ, ниг. WaveformBackfillJob барои
/// ҳамин алгу). Дар навбати "media" (файли диск мехонад).
/// </summary>
[Queue("media")]
public class HtmlMediaCleanupJob(
    AppDbContext db,
    IConfiguration configuration,
    IWebHostEnvironment env,
    ILogger<HtmlMediaCleanupJob> logger)
{
    private const int BatchSize = 200;
    private const int SampleBytes = 512;

    public async Task RunAsync(CancellationToken ct)
    {
        var rootPath = UploadsPathResolver.ResolveRootPath(configuration, env);

        var candidates = await db.Messages
            .Where(m => m.MediaUrl != null && m.MediaDownloadError == null && m.MediaDeletedAt == null)
            .OrderBy(m => m.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return;

        var cleanedCount = 0;
        foreach (var message in candidates)
        {
            var fullPath = Path.Combine(rootPath, message.MediaUrl!);
            if (!File.Exists(fullPath))
                continue;

            byte[] sample;
            try
            {
                await using var stream = File.OpenRead(fullPath);
                sample = new byte[Math.Min(stream.Length, SampleBytes)];
                var read = await stream.ReadAsync(sample, ct);
                if (read < sample.Length)
                    sample = sample[..read];
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "HtmlMediaCleanupJob: хондани файли паёми {MessageId} ноком шуд — гузаронда мешавад.", message.Id);
                continue;
            }

            if (!HtmlContentSniffer.LooksLikeHtml(sample))
                continue;

            logger.LogWarning(
                "HtmlMediaCleanupJob: паёми {MessageId} воқеан HTML буд (на медиа) — тоза мешавад, барқарор кардан имконнопазир.",
                message.Id);

            TryDelete(fullPath, message.Id);
            if (message.ThumbnailUrl is not null)
            {
                TryDelete(Path.Combine(rootPath, message.ThumbnailUrl), message.Id);
                message.ThumbnailUrl = null;
            }

            message.MediaUrl = null;
            message.MediaDownloadError =
                "URL-и медиа то боркунӣ мӯҳлаташ гузашта буд — сервер саҳифаи хатогӣ баргардонда буд, ки хатоан ҳамчун медиа сабт шуда буд. Файл гум шудааст, барқарор кардан имконнопазир аст.";
            cleanedCount++;
        }

        if (cleanedCount == 0)
            return;

        await db.SaveChangesAsync(ct);
        logger.LogInformation("HtmlMediaCleanupJob: {Count} паёми вайроншуда тоза шуд.", cleanedCount);
    }

    private void TryDelete(string fullPath, Guid messageId)
    {
        try
        {
            if (File.Exists(fullPath))
                File.Delete(fullPath);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "HtmlMediaCleanupJob: нест кардани файли паёми {MessageId} ноком шуд.", messageId);
        }
    }
}
