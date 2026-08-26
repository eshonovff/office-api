using Microsoft.EntityFrameworkCore;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Features.Conversations;
using Office.Api.Realtime;

namespace Office.Api.Channels;

/// <summary>
/// Hangfire recurring job — чати кушода (на Closed)-ро, ки таъиншудаи он муддате (аз рӯи
/// конфигуратсия, пешфарз 3 соат) дар ҳамон чат чизе нафиристодааст, аз нав ба pool
/// бармегардонад. Танҳо чати кушода — Closed ҳеҷ гоҳ ламс намешавад. Лаҳзаи "таъин шудан"
/// аз ConversationAssignmentEvent гирифта мешавад (item 3), на сутуни алоҳида дар Conversation.
/// </summary>
public class ConversationAutoReleaseJob(
    AppDbContext db,
    IConfiguration configuration,
    IInboxEventPublisher events,
    ILogger<ConversationAutoReleaseJob> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var threshold = TimeSpan.FromHours(configuration.GetValue("Inbox:AutoReleaseAfterHours", 3));
        var now = DateTimeOffset.UtcNow;

        var candidates = await db.Conversations
            .Include(c => c.Channel)
            .Include(c => c.Assignee)
            .Where(c => c.AssignedTo != null && c.Status != ConversationStatus.Closed)
            .ToListAsync(ct);

        var releasedCount = 0;
        foreach (var conversation in candidates)
        {
            var assignedAt = await db.ConversationAssignmentEvents
                .Where(e => e.ConversationId == conversation.Id && e.ToUserId == conversation.AssignedTo)
                .OrderByDescending(e => e.CreatedAt)
                .Select(e => (DateTimeOffset?)e.CreatedAt)
                .FirstOrDefaultAsync(ct);

            // Агар таърих набошад (масалан таъиноти пеш аз батчи 2-и collaboration),
            // lastMessageAt ҳамчун fallback — беҳтар аз ин ки ҳеҷ гоҳ release нашавад.
            var effectiveAssignedAt = assignedAt ?? conversation.LastMessageAt ?? conversation.CreatedAt;

            var lastActivityByAssignee = await db.Messages
                .Where(m =>
                    m.ConversationId == conversation.Id &&
                    m.Direction == MessageDirection.Outbound &&
                    m.SentByUserId == conversation.AssignedTo)
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => (DateTimeOffset?)m.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (!ConversationAssignmentPolicy.ShouldAutoRelease(
                    conversation.Status, effectiveAssignedAt, lastActivityByAssignee, now, threshold))
            {
                continue;
            }

            var previousAssigneeId = conversation.AssignedTo;
            var previousAssigneeName = conversation.Assignee?.FullName;

            conversation.AssignedTo = null;
            conversation.Assignee = null;

            db.ConversationAssignmentEvents.Add(new ConversationAssignmentEvent
            {
                Id = Guid.CreateVersion7(),
                ConversationId = conversation.Id,
                FromUserId = previousAssigneeId,
                FromUserName = previousAssigneeName,
                ToUserId = null,
                ToUserName = null,
                Reason = ConversationAssignmentReason.AutoReleased,
                CreatedAt = now,
            });

            await db.SaveChangesAsync(ct);

            var dto = ConversationsEndpoints.ToDetail(conversation);
            await events.ConversationAssignedAsync(conversation.ChannelId, null, dto, ct);

            releasedCount++;
        }

        if (releasedCount > 0)
            logger.LogInformation("ConversationAutoReleaseJob: {Count} чат бе таъин монд.", releasedCount);
    }
}
