using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Office.Api.Channels.Instagram;
using Office.Api.Data;
using Office.Api.Data.Entities;

using Office.Api.Channels.Comments;

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
public class CommentAutomationJob(AppDbContext db, InstagramProvider instagramProvider, ILogger<CommentAutomationJob> logger)
{
    public async Task RunAsync(Guid automationRunId, CancellationToken ct)
    {
        var run = await db.AutomationRuns.Include(r => r.Rule).ThenInclude(rule => rule.Channel)
            .FirstOrDefaultAsync(r => r.Id == automationRunId, ct);
        if (run is null)
            return;

        var channel = run.Rule.Channel;
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

        var branch = AutomationBranchSelector.Select(actionConfig, run.FollowCheckResult);

        // Round-robin аз рӯи шумораи run-ҳои қаблии ин rule (пеш аз ин run сохта шудаанд) —
        // ниг. CommentReplySelector: детерминистӣ, ниёз ба сутуни иловагӣ надорад. Ҳисоби
        // умумии rule аст (на ҷудо барои ҳар шоха) — рӯи CommentReplies-и шохаи интихобшуда амал мекунад.
        var priorRunCount = await db.AutomationRuns.CountAsync(r => r.RuleId == run.RuleId && r.CreatedAt < run.CreatedAt, ct);
        var replyText = CommentReplySelector.Select(branch.CommentReplies, priorRunCount);

        try
        {
            await instagramProvider.ReplyToCommentAsync(channel, run.TriggerExternalId, replyText, ct);
            run.CommentReplyStatus = AutomationRunStatus.Sent;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AutomationRun {RunId}: ҷавоби ҷамъиятӣ ноком шуд", run.Id);
            run.CommentReplyStatus = AutomationRunStatus.Failed;
            run.Error = ex.Message;
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
    }
}
