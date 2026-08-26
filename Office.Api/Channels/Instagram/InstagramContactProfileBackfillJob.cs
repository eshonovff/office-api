using Hangfire;
using Microsoft.EntityFrameworkCore;
using Office.Api.Data;
using Office.Api.Data.Entities;

namespace Office.Api.Channels.Instagram;

/// <summary>
/// Чатҳои Instagram-и пеш аз ин илова шудани ContactUsername сохта шуда буданд — то ҳол танҳо
/// ID-и рақамӣ (IGSID) доранд. Ин job як маротиба (вале recurring/идемпотентӣ, ниг. поён) ҳар
/// чат бе profile-ро аз Meta мегирад. Дар навбати "media-maintenance" (мисли HtmlMediaCleanupJob/
/// WaveformBackfillJob) — на мижози зинда мунтазир аст.
/// </summary>
[Queue("media-maintenance")]
public class InstagramContactProfileBackfillJob(
    AppDbContext db,
    InstagramProvider provider,
    ILogger<InstagramContactProfileBackfillJob> logger)
{
    // Дар як иҷро — агар боқимонда бошад, "Trigger now"-и дигар онҳоро идома медиҳад (идемпотентӣ:
    // WHERE-и поён ҳеҷ гоҳ чате, ки аллакай коркард шудааст, дубора намегирад).
    private const int BatchSize = 500;
    private const int SlowdownThresholdPercent = 75;
    private static readonly TimeSpan NormalDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan SlowDelay = TimeSpan.FromSeconds(3);

    public async Task RunAsync(CancellationToken ct)
    {
        // ContactUsername == null: ҳанӯз маълумот надорад (набояд аз нав нависем, агар дошта бошад).
        // ContactProfileFetchedAt == null: ҳанӯз кӯшиш НАКАРДА ШУДААСТ — идемпотентӣ маҳз аз ин
        // меояд: чате, ки Meta барояш ҳеҷ чиз надод (масалан корбар ҳисобашро нест кардааст), боз
        // ҳам "коркардшуда" ҳисоб мешавад, то ҳар иҷро аз нав кӯшиш накунад.
        var conversations = await db.Conversations
            .Include(c => c.Channel)
            .Where(c => c.Channel.Type == ChannelType.Instagram && c.ContactUsername == null && c.ContactProfileFetchedAt == null)
            .OrderBy(c => c.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (conversations.Count == 0)
        {
            logger.LogInformation("InstagramContactProfileBackfillJob: ҳама чатҳои Instagram аллакай коркард шудаанд.");
            return;
        }

        var filled = 0;
        var empty = 0;
        var failed = 0;
        var delay = NormalDelay;

        foreach (var conversation in conversations)
        {
            try
            {
                var (profile, callVolumePercent) = await provider.GetContactProfileWithUsageAsync(conversation.Channel, conversation.ExternalId, ct);
                // Новобаста аз натиҷа — то дафъаи оянда ин чат дубора кӯшиш нашавад.
                conversation.ContactProfileFetchedAt = DateTimeOffset.UtcNow;

                if (profile.Name is not null || profile.Username is not null || profile.AvatarUrl is not null)
                {
                    // ??= — агар ягон майдон аллакай пур бошад (набояд рӯй диҳад, чунки WHERE
                    // ContactUsername==null-ро месанҷад, вале ContactName метавонад аз webhook
                    // омада бошад), онро аз нав нанависем.
                    conversation.ContactName ??= profile.Name;
                    conversation.ContactAvatarUrl ??= profile.AvatarUrl;
                    conversation.ContactUsername = profile.Username;
                    filled++;
                }
                else
                {
                    // Meta ҷавоб дод (200 ё хатои log-шуда), вале ҳеҷ маълумот надод — эҳтимол
                    // корбар ҳисобашро нест кардааст ё чатро тарк кардааст. ContactProfileFetchedAt
                    // аллакай боло гузошта шуд, пас чат бо ID мемонад, вале абадан кӯшиш намешавад.
                    empty++;
                }

                await db.SaveChangesAsync(ct);
                delay = callVolumePercent is >= SlowdownThresholdPercent ? SlowDelay : NormalDelay;
            }
            catch (Exception ex)
            {
                // ContactProfileFetchedAt ҚАСДАН гузошта НАМЕШАВАД — ин хатои ғайричашмдошт
                // (масалан шабака) аст, на "Meta маълумот надод"; дафъаи оянда бояд боз кӯшиш кунад.
                // Як чати ноком набояд боқимондаро бас кунад.
                failed++;
                logger.LogWarning(ex, "InstagramContactProfileBackfillJob: чат {ConversationId} гузаронда шуд.", conversation.Id);
            }

            await Task.Delay(delay, ct);
        }

        logger.LogInformation(
            "InstagramContactProfileBackfillJob: {Total} чат коркард шуд — пур: {Filled}, холӣ (Meta маълумот надод): {Empty}, хато: {Failed}.",
            conversations.Count, filled, empty, failed);
    }
}
