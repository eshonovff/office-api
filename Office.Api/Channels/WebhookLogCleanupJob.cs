using Microsoft.EntityFrameworkCore;
using Office.Api.Data;

namespace Office.Api.Channels;

/// <summary>Тозакунии худкори WebhookLog-ҳои аз 30 рӯз калонтар (Hangfire recurring job).</summary>
public class WebhookLogCleanupJob(AppDbContext db, ILogger<WebhookLogCleanupJob> logger)
{
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(30);

    public async Task RunAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow - RetentionPeriod;
        var deleted = await db.WebhookLogs
            .Where(w => w.ReceivedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
            logger.LogInformation("Deleted {Count} webhook log(s) older than {Retention}.", deleted, RetentionPeriod);
    }
}
