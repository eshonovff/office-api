using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Office.Api.Channels.Comments;
using Office.Api.Channels.Flows;
using Office.Api.Channels.Instagram;
using Office.Api.Data;
using Office.Api.Data.Entities;

namespace Office.Api.Channels.Automation;

/// <summary>
/// Иҷрои воқеии як AutomationRun: ҷавоби ҷамъиятӣ ба коментарий, баъд DM (private reply).
/// Ҳар кадом мустақилона хато сабт мекунад — нокомии яке дигареро блок намекунад (масалан
/// public reply муваффақ, вале DM аз сабаби гузаштани 7-рӯза ноком шавад). Хатогиҳои Graph API
/// қасдан ба берун партофта НАМЕШАВАНД (catch дар ҳамин ҷо) — вагарна [AutomaticRetry]-и поён
/// кӯшиши дуюм мекард ва ҷавоби ҷамъиятии АЛЛАКАЙ фиристодашударо такрор мефиристод.
/// [AutomaticRetry] танҳо барои хатогиҳои беруни ин ду catch (DB/JSON) боқӣ мемонад.
/// </summary>
[AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 300, 1800])]
public class CommentAutomationJob(
    AppDbContext db, InstagramProvider instagramProvider, ICommentEventPublisher commentEvents, ILogger<CommentAutomationJob> logger)
{
    public async Task RunAsync(Guid automationRunId, CancellationToken ct)
    {
        var run = await db.AutomationRuns.Include(r => r.Rule).ThenInclude(rule => rule.Channel)
            .FirstOrDefaultAsync(r => r.Id == automationRunId, ct);
        if (run is null)
            return;

        var channel = run.Rule.Channel;

        // Checked again here: a retry may run long after the comment, and a мизоҷ's plan may
        // have ended since (company channels always pass).
        if (!await AutomationRunGate.CanRunAsync(channel.Id, db, ct))
        {
            run.CommentReplyStatus = AutomationRunStatus.Disabled;
            run.DmStatus = AutomationRunStatus.Disabled;
            run.Error = "Тариф фаъол нест ё аккаунт ҷудо шудааст — иҷро нашуд.";
            await db.SaveChangesAsync(ct);
            return;
        }

        AutomationActionConfig actionConfig;
        AutomationConditionConfig conditionConfig;
        try
        {
            actionConfig = JsonSerializer.Deserialize<AutomationActionConfig>(run.Rule.ActionConfigJson) ??
                throw new JsonException("null");
            conditionConfig = JsonSerializer.Deserialize<AutomationConditionConfig>(run.Rule.ConditionConfigJson) ??
                throw new JsonException("null");
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "AutomationRun {RunId}: action_config/condition_config вайрон аст", run.Id);
            run.CommentReplyStatus = AutomationRunStatus.Failed;
            run.DmStatus = AutomationRunStatus.Failed;
            run.Error = "action_config/condition_config вайрон аст: " + ex.Message;
            await db.SaveChangesAsync(ct);
            return;
        }

        // Фазаи 11: агар rule тасдиқи обунаро талаб кунад, пеш аз интихоби шоха тафтиш карда
        // мешавад. Натиҷа (аз ҷумла Unknown — токен афтод ё Meta хато дод) дар run сабт мешавад,
        // на танҳо истифода — то дар омор дида шавад. requiresFollow=false → null (тафтиш нашуд).
        run.FollowCheckResult = conditionConfig.RequiresFollow
            ? await instagramProvider.CheckFollowStatusAsync(channel, run.ActorExternalId, ct)
            : null;

        // Told "follow us, then comment again" within the cooldown and still not following: no
        // second ask — or every further comment would get the same public "please follow".
        if (run.FollowCheckResult == FollowCheckResult.NotFollowing && await WasAskedToFollowAsync(run, ct))
        {
            run.CommentReplyStatus = AutomationRunStatus.SkippedCooldown;
            run.DmStatus = AutomationRunStatus.SkippedCooldown;
            await db.SaveChangesAsync(ct);
            return;
        }

        var branch = AutomationBranchSelector.Select(actionConfig, run.FollowCheckResult);

        // Round-robin аз рӯи шумораи run-ҳои қаблии ин rule (пеш аз ин run сохта шудаанд) —
        // ниг. CommentReplySelector: детерминистӣ, ниёз ба сутуни иловагӣ надорад. Ҳисоби
        // умумии rule аст (на ҷудо барои ҳар шоха) — рӯи CommentReplies-и шохаи интихобшуда амал мекунад.
        var priorRunCount = await db.AutomationRuns.CountAsync(r => r.RuleId == run.RuleId && r.CreatedAt < run.CreatedAt, ct);
        var replyText = CommentReplySelector.Select(branch.CommentReplies, priorRunCount);

        // The stored comment (the comments page) — null if it came without a post id.
        var comment = await db.InstagramComments
            .FirstOrDefaultAsync(c => c.ChannelId == channel.Id && c.ExternalId == run.TriggerExternalId, ct);
        // Instagram threads are one level deep: a reply to a reply goes under the top comment.
        var replyTarget = comment?.ParentExternalId ?? run.TriggerExternalId;

        try
        {
            var replyId = await instagramProvider.ReplyToCommentAsync(channel, replyTarget, replyText, ct);
            run.CommentReplyStatus = AutomationRunStatus.Sent;
            if (comment is not null)
            {
                comment.AutoReplyError = null;
                if (replyId is not null)
                    await CommentLedger.RecordAutomatedReplyAsync(db, channel, comment, replyTarget, replyId, replyText, DateTimeOffset.UtcNow, ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AutomationRun {RunId}: ҷавоби ҷамъиятӣ ноком шуд", run.Id);
            run.CommentReplyStatus = AutomationRunStatus.Failed;
            run.Error = ex.Message;
            // Shown on the comments page next to the comment: Meta's reason, never a stack trace.
            if (comment is not null)
            {
                var reason = ex is GraphApiException ? ex.Message : "Instagram ҷавобро қабул накард.";
                comment.AutoReplyError = reason.Length > 500 ? reason[..500] : reason;
            }
        }

        if (string.IsNullOrEmpty(branch.DmText))
        {
            // Корбар барои ин шоха DM танзим накардааст — қасдан фиристода намешавад (танҳо
            // ҷавоби ҷамъиятӣ кофист). Ин холат аз хатогӣ фарқ мекунад — Error холӣ мемонад.
            run.DmStatus = AutomationRunStatus.Disabled;
        }
        else
        {
            try
            {
                var button = string.IsNullOrEmpty(branch.DmButtonUrl) || string.IsNullOrEmpty(branch.DmButtonTitle)
                    ? null
                    : new InstagramSendButton(branch.DmButtonTitle, InstagramSendButton.TypeWebUrl, branch.DmButtonUrl, null);
                await instagramProvider.SendPrivateReplyAsync(channel, run.TriggerExternalId, branch.DmText, button, ct);
                run.DmStatus = AutomationRunStatus.Sent;
                await CommentLedger.MarkPrivateReplySentAsync(db, channel.Id, run.TriggerExternalId, DateTimeOffset.UtcNow, ct);
            }
            catch (Exception ex)
            {
                // Маъмултарин сабаб: 7 рӯз гузаштааст ё private reply аллакай як бор фиристода
                // шудааст — Meta бо хатои возеҳ рад мекунад (ниг. шарҳи SendPrivateReplyAsync).
                logger.LogError(ex, "AutomationRun {RunId}: DM (private reply) ноком шуд", run.Id);
                run.DmStatus = AutomationRunStatus.Failed;
                run.Error = run.Error is null ? ex.Message : $"{run.Error} | DM: {ex.Message}";
            }
        }

        await db.SaveChangesAsync(ct);
        if (comment is not null)
            await commentEvents.CommentsChangedAsync(channel.Id, comment.MediaExternalId, ct);
    }

    /// <summary>An earlier run for this person on this post, inside the cooldown, already asked them to follow.</summary>
    private Task<bool> WasAskedToFollowAsync(AutomationRun run, CancellationToken ct)
    {
        var since = run.CreatedAt - TimeSpan.FromMinutes(run.Rule.CooldownMinutes);
        return db.AutomationRuns.AnyAsync(r =>
            r.Id != run.Id && r.RuleId == run.RuleId && r.ActorExternalId == run.ActorExternalId
            && r.TargetMediaExternalId == run.TargetMediaExternalId
            && r.CreatedAt >= since && r.CreatedAt < run.CreatedAt
            && r.FollowCheckResult == FollowCheckResult.NotFollowing
            && r.CommentReplyStatus != AutomationRunStatus.SkippedCooldown, ct);
    }
}
