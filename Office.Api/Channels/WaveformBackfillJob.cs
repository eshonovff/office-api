using Hangfire;
using Microsoft.EntityFrameworkCore;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Media;

namespace Office.Api.Channels;

/// <summary>
/// Пикҳои шакли мавҷ барои Audio-и мавҷуда, ки пеш аз ин вазифа сохта нашуда буданд
/// (паёмҳои кӯҳна, ё коркарде ки бо хатогӣ гузашт). Recurring, на "як маротиба ва
/// тамом" — ҳар рӯз танҳо он чи ҳанӯз WaveformPeaks надорад ва файлаш дар диск
/// ҳаст (MediaDeletedAt=null) коркард мекунад; агар ҳама аллакай сохта шуда
/// бошанд, дархости холӣ бармегардонад (арзон, бехатар барои иҷрои такрорӣ).
/// Дар навбати "media-maintenance"-и Hangfire (на "media") — на мижози зинда мунтазир аст,
/// пас набояд боркунии медиаи тозаро дар навбати маҳдуди 2-worker-и "media" ба таъхир андозад.
/// </summary>
[Queue("media-maintenance")]
public class WaveformBackfillJob(
    AppDbContext db,
    IMediaProcessor mediaProcessor,
    IConfiguration configuration,
    IWebHostEnvironment env,
    ILogger<WaveformBackfillJob> logger)
{
    private const int BatchSize = 100;

    public async Task RunAsync(CancellationToken ct)
    {
        var rootPath = UploadsPathResolver.ResolveRootPath(configuration, env);

        var candidates = await db.Messages
            .Where(m => m.Type == MessageType.Audio && m.MediaUrl != null && m.MediaDeletedAt == null && m.WaveformPeaks == null)
            .OrderBy(m => m.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return;

        var processedCount = 0;
        foreach (var message in candidates)
        {
            var fullPath = Path.Combine(rootPath, message.MediaUrl!);
            if (!File.Exists(fullPath))
                continue;

            try
            {
                var peaks = await mediaProcessor.GenerateWaveformPeaksAsync(fullPath, WaveformPeakCalculator.DefaultPeakCount, ct);
                message.WaveformPeaks = [.. peaks];
                processedCount++;
            }
            catch (MediaProcessingException ex)
            {
                logger.LogWarning(ex, "WaveformBackfillJob: сохтани пикҳо барои паёми {MessageId} ноком шуд", message.Id);
            }
        }

        if (processedCount == 0)
            return;

        await db.SaveChangesAsync(ct);
        logger.LogInformation("WaveformBackfillJob: {Count} паём коркард шуд.", processedCount);
    }
}
