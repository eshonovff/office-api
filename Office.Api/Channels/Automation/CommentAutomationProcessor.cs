using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Office.Api.Channels.Instagram;
using Office.Api.Data;
using Office.Api.Data.Entities;

namespace Office.Api.Channels.Automation;

/// <summary>
/// Коркарди як webhook-и коментарии Instagram: филтри ҳалқа → интихоби аввалин rule-и мувофиқ →
/// cooldown → сабти AutomationRun → enqueue-и CommentAutomationJob (ки воқеан бо Meta сӯҳбат
/// мекунад). Ин синф ҳеҷ дархости HTTP намекунад — танҳо DB, пас pure-тест-пазир нест
/// (DbContext лозим аст), вале ҳеҷ Graph API call надорад — санҷиш бо DB-и in-memory кофист.
/// </summary>
public class CommentAutomationProcessor(AppDbContext db, IBackgroundJobClient backgroundJobs, ILogger<CommentAutomationProcessor> logger)
{
    public async Task ProcessAsync(Channel channel, ParsedCommentEvent evt, CancellationToken ct)
    {
        // Филтри ҳалқа: агар аккаунти бизнес ба коментарии худаш ҷавоб дода бошад (масалан
        // худи ин автоматизатсия), Meta онро низ ҳамчун webhook мефиристад — бе ин система ба
        // ҷавоби худ ҷавоб медиҳад, беохир. Ҳатто сабт намешавад — ин run нест.
        if (evt.ActorExternalId == channel.ExternalId)
            return;

        // Идемпотентӣ: агар ин comment_id аллакай коркард шуда бошад (масалан Meta webhook-ро
        // такрор фиристод — воқеаи маъмул ҳангоми таъхир дар ҷавоб), дубора накун. Ин ҳамчунин
        // хатари follow-check/private-reply-и такрориро пешгирӣ мекунад (Meta барои як comment_id
        // танҳо ЯК private reply иҷозат медиҳад — ниг. InstagramProvider.SendPrivateReplyAsync).
        if (await db.AutomationRuns.AnyAsync(r => r.TriggerExternalId == evt.CommentId, ct))
            return;

        var rules = await db.AutomationRules
            .Where(r => r.ChannelId == channel.Id && r.IsActive && r.TriggerType == "instagram_comment")
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);

        foreach (var rule in rules)
        {
            AutomationTriggerConfig triggerConfig;
            try
            {
                triggerConfig = JsonSerializer.Deserialize<AutomationTriggerConfig>(rule.TriggerConfigJson) ??
                    throw new JsonException("null");
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "AutomationRule {RuleId}: trigger_config вайрон аст, рад карда шуд", rule.Id);
                continue;
            }

            var match = CommentAutomationMatcher.Match(triggerConfig, evt.Text, evt.MediaId);
            if (!match.Matched)
                continue;

            // Якто rule дар як комментарий: аввалини мувофиқ интихоб мешавад, дигарҳо
            // санҷида НАМЕШАВАНД — ҳатто агар ин яктo дар cooldown бошад (соддагии V1).
            var lastRun = await db.AutomationRuns
                .Where(r => r.RuleId == rule.Id && r.ActorExternalId == evt.ActorExternalId && r.TargetMediaExternalId == evt.MediaId)
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync(ct);

            var now = DateTimeOffset.UtcNow;
            var inCooldown = lastRun is not null && now - lastRun.CreatedAt < TimeSpan.FromMinutes(rule.CooldownMinutes);

            var run = new AutomationRun
            {
                Id = Guid.CreateVersion7(),
                RuleId = rule.Id,
                TriggerExternalId = evt.CommentId,
                ActorExternalId = evt.ActorExternalId,
                TargetMediaExternalId = evt.MediaId,
                MatchedKeyword = match.MatchedKeyword,
                CommentReplyStatus = inCooldown ? AutomationRunStatus.SkippedCooldown : AutomationRunStatus.Pending,
                DmStatus = inCooldown ? AutomationRunStatus.SkippedCooldown : AutomationRunStatus.Pending,
                CreatedAt = now,
            };
            db.AutomationRuns.Add(run);
            await db.SaveChangesAsync(ct);

            if (!inCooldown)
                backgroundJobs.Enqueue<CommentAutomationJob>(j => j.RunAsync(run.Id, CancellationToken.None));

            return;
        }
    }
}
