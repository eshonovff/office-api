using Microsoft.EntityFrameworkCore;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;

namespace Office.Api.Features.DataDeletion;

/// <summary>
/// Carries out one Meta data-deletion request: every Instagram channel of that user — company's
/// or a мизоҷ's — is erased with everything under it. Runs in Hangfire (no HTTP caller → System
/// tenant scope, so channels of every owner are visible, as they must be here).
/// </summary>
public class DataDeletionJob(AppDbContext db, IConfiguration configuration, IWebHostEnvironment env, ILogger<DataDeletionJob> logger)
{
    public async Task RunAsync(Guid requestId, CancellationToken ct)
    {
        var request = await db.DataDeletionRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null || request.Status == DataDeletionStatus.Completed)
            return;

        var userId = request.MetaUserId;
        // Meta's docs don't pin down which of Instagram's two ids it sends, so both are matched:
        // the app-scoped id (recorded at connect) and the business id (ExternalId).
        var channelIds = userId is null
            ? []
            : await db.Channels
                .Where(c => c.Type == ChannelType.Instagram && (c.MetaAppScopedUserId == userId || c.ExternalId == userId))
                .Select(c => c.Id)
                .ToListAsync(ct);

        request.Status = DataDeletionStatus.Completed;
        request.ChannelsDeleted = channelIds.Count;
        request.CompletedAt = DateTimeOffset.UtcNow;
        request.MetaUserId = null;

        // The request's own update rides in the eraser's single SaveChanges: the channels and the
        // "completed" mark commit together or not at all.
        await ChannelDataEraser.EraseAsync(db, channelIds, ct);
        await db.SaveChangesAsync(ct); // when nothing matched, the eraser saved nothing

        ChannelDataEraser.DeleteMediaFolders(UploadsPathResolver.ResolveRootPath(configuration, env), channelIds);
        logger.LogInformation("Data deletion {RequestId}: {Count} channel(s) erased", request.Id, channelIds.Count);
    }
}
