using Hangfire;
using Microsoft.EntityFrameworkCore;
using Office.Api.Common;
using Office.Api.Data;

namespace Office.Api.Channels.ContactProfiles;

/// <summary>
/// Keeps our own copy of a contact's picture (ContactAvatarDownloader) and drops the old one. A
/// contact deleted meanwhile keeps nothing: its copy is removed again.
/// </summary>
[Queue("media")]
[AutomaticRetry(Attempts = 0)]
public class ContactAvatarJob(
    AppDbContext db, ContactAvatarDownloader downloader, IConfiguration configuration, IWebHostEnvironment env)
{
    public async Task DownloadAsync(Guid conversationId, CancellationToken ct)
    {
        var conversation = await db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId, ct);
        if (conversation?.ContactAvatarUrl is null)
            return;

        var root = UploadsPathResolver.ResolveRootPath(configuration, env);
        var saved = await downloader.SaveAsync(conversation.ContactAvatarUrl, conversation.ChannelId, conversation.Id, root, ct);
        if (saved is null)
            return;

        var previous = conversation.ContactAvatarPath;
        conversation.ContactAvatarPath = saved;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            Delete(root, saved); // deleted while we were downloading
            return;
        }

        if (previous is not null && previous != saved)
            Delete(root, previous);
    }

    private static void Delete(string root, string relativePath)
    {
        var path = SafeUploadsPath.TryResolve(root, relativePath);
        if (path is not null && File.Exists(path))
            File.Delete(path);
    }
}
