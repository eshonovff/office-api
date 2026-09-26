using Hangfire;
using Microsoft.EntityFrameworkCore;
using Office.Api.Data;
using Office.Api.Data.Entities;

namespace Office.Api.Channels.ContactProfiles;

/// <summary>
/// Daily: Instagram and Facebook chats of the last 90 days still without a name or a picture of
/// our own — the chats the account started before the person wrote back, and every picture whose
/// Meta link has expired. Newest first, 200 a run, each asked again at most once a day.
/// </summary>
[Queue("media-maintenance")]
public class ContactProfileBackfillJob(
    AppDbContext db, IChannelProviderFactory providers, IBackgroundJobClient jobs, ILogger<ContactProfileBackfillJob> logger)
{
    public const int BatchSize = 200;
    public static readonly TimeSpan ActiveWithin = TimeSpan.FromDays(90);
    public static readonly TimeSpan AskAgainAfter = TimeSpan.FromDays(1);

    /// <summary>Between two questions to Meta — gentle on the account's rate limit.</summary>
    public TimeSpan Pause { get; init; } = TimeSpan.FromMilliseconds(300);

    public async Task RunAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var activeSince = now - ActiveWithin;
        var askedBefore = now - AskAgainAfter;
        var chats = await db.Conversations
            .Include(c => c.Channel)
            .Where(c => (c.Channel.Type == ChannelType.Instagram || c.Channel.Type == ChannelType.Facebook) &&
                        c.Channel.IsActive && !c.Channel.RequiresReconnect &&
                        c.Channel.CredentialsEncrypted != null && c.Channel.CredentialsEncrypted != "")
            .Where(c => (c.LastMessageAt ?? c.CreatedAt) >= activeSince)
            .Where(c => c.ContactAvatarPath == null ||
                        (c.Channel.Type == ChannelType.Instagram ? c.ContactUsername == null : c.ContactName == null))
            .Where(c => c.ContactProfileFetchedAt == null || c.ContactProfileFetchedAt < askedBefore)
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        var pictures = 0;
        foreach (var chat in chats)
        {
            try
            {
                var hasPicture = await ContactProfileUpdater.RefreshAsync(db, providers.GetProvider(chat.Channel.Type), chat.Channel, chat, now, ct);
                await db.SaveChangesAsync(ct); // before the picture job reads the link
                if (hasPicture)
                {
                    jobs.Enqueue<ContactAvatarJob>(j => j.DownloadAsync(chat.Id, CancellationToken.None));
                    pictures++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "ContactProfileBackfillJob: чати {ConversationId} гузаронда шуд.", chat.Id);
                // Noted as asked all the same: tomorrow it is tried again — until then it must not
                // stand at the head of the queue, newest first, in front of every other chat.
                try
                {
                    chat.ContactProfileFetchedAt = now;
                    await db.SaveChangesAsync(ct);
                }
                catch (Exception saveEx) when (saveEx is not OperationCanceledException)
                {
                    logger.LogWarning(saveEx, "ContactProfileBackfillJob: чати {ConversationId} сабт нашуд.", chat.Id);
                }
            }

            await Task.Delay(Pause, ct);
        }

        logger.LogInformation("ContactProfileBackfillJob: {Count} чат пурсида шуд, {Pictures} сурат ба навбат.", chats.Count, pictures);
    }
}
