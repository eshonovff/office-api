using System.Linq.Expressions;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Office.Api.Channels;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;

namespace Office.Api.Features.Dashboard;

/// <summary>
/// Ҳисоби воқеии GET /api/dashboard — аз DashboardEndpoints ҷудо, то бе HTTP/кэш санҷида
/// шавад (ҳамон сабаби InstagramContactProfileBackfillJob як class-и алоҳида аст, на қисми
/// static-и endpoint). Ҳама агрегат дар сатҳи БД (CountAsync/Take), на LINQ-to-Objects.
/// </summary>
public class DashboardQueryService(AppDbContext db, IChannelAccessGuard channelAccess)
{
    private const int PreviewLimit = 5;
    private const int ClosingWindowHours = 2;
    private const int FailedMessagesLookbackDays = 7;
    private const int CredentialsExpiringSoonDays = 10;

    public async Task<DashboardResponse> ComputeAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Як маротиба — на дар ҳар метод алоҳида: ApplyAccessFilterAsync худаш як дархости DB
        // мекунад (only_assigned-и корбар), пас IQueryable-и натиҷаро ин ҷо ЯК бор мегирем ва
        // барои ҳар се нишондиҳанда (closingWindows/unassigned/failedMessages) аз нав истифода
        // мебарем — .Where()-и минбаъда ин дархостро такрор намекунад, танҳо expression tree
        // илова мекунад.
        var accessibleConversations = await channelAccess.ApplyAccessFilterAsync(db.Conversations.AsNoTracking(), principal, ct);

        // "Имрӯз" — вақти маҳаллӣ (UTC+5), на UTC-и хом; як бор ин ҷо, то Блоки 1 (overdueTasks)
        // ва Блоки 2 (myTasksToday/Overdue) ҳамон "имрӯз"-ро истифода баранд (ниг. OfficeLocalDate).
        var today = OfficeLocalDate.Today(now);

        var closingWindows = await GetClosingWindowsAsync(accessibleConversations, now, ct);
        var unassigned = await GetUnassignedCountAsync(accessibleConversations, ct);
        var failedMessages = await GetFailedMessagesAsync(accessibleConversations, now, ct);
        var channelIssues = await GetChannelIssuesAsync(principal, now, ct);
        var overdueTasks = await GetOverdueTasksAsync(principal, today, ct);
        var myWork = await GetMyWorkAsync(principal, accessibleConversations, today, ct);

        return new DashboardResponse(
            new ActionRequiredDto(closingWindows, unassigned, failedMessages, channelIssues, overdueTasks),
            myWork,
            ChannelAccessGuard.CanSeeAllChannels(principal));
    }

    private static async Task<ClosingWindowsSummary> GetClosingWindowsAsync(
        IQueryable<Conversation> accessibleConversations, DateTimeOffset now, CancellationToken ct)
    {
        var threshold = now.AddHours(ClosingWindowHours);

        // "Кушода" — тарзи ҳозираи коди барнома (ConversationAutoReleaseJob, ConversationAssignmentPolicy):
        // Status != Closed, на рӯйхати сахти {New, InProgress, Waiting}.
        var query = accessibleConversations.Where(c =>
            c.Status != ConversationStatus.Closed &&
            c.WindowExpiresAt != null && c.WindowExpiresAt > now && c.WindowExpiresAt <= threshold);

        var count = await query.CountAsync(ct);
        var items = await query
            .OrderBy(c => c.WindowExpiresAt)
            .Take(PreviewLimit)
            .Select(c => new ClosingWindowItem(c.Id, c.ContactName ?? c.ExternalId, c.WindowExpiresAt!.Value))
            .ToListAsync(ct);

        return new ClosingWindowsSummary(count, items);
    }

    private static Task<int> GetUnassignedCountAsync(IQueryable<Conversation> accessibleConversations, CancellationToken ct) =>
        accessibleConversations.CountAsync(c => c.Status == ConversationStatus.New && c.AssignedTo == null, ct);

    private async Task<FailedMessagesSummary> GetFailedMessagesAsync(
        IQueryable<Conversation> accessibleConversations, DateTimeOffset now, CancellationToken ct)
    {
        var since = now.AddDays(-FailedMessagesLookbackDays);

        // accessibleConversations дар ин ҷо ба EXISTS-и subquery табдил меёбад — на ID-ҳоро
        // пеш ба хотира мекашем (рӯйхати чат метавонад калон бошад), на N+1.
        var query = db.Messages.AsNoTracking()
            .Where(m => m.DeliveryStatus == MessageDeliveryStatus.Failed && m.CreatedAt >= since && m.FailureReason != null)
            .Where(m => accessibleConversations.Any(c => c.Id == m.ConversationId));

        var count = await query.CountAsync(ct);

        // GROUP BY дар SQL (на LINQ-to-Objects) — fbtrace_id (дар FailureDetail) ягона аст,
        // вале FailureCode такрор мешавад: се навъи воқеии мушкил, на 20 гурӯҳи "беном".
        var groups = await query
            .GroupBy(m => m.FailureCode)
            .Select(g => new { FailureCode = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .Take(PreviewLimit)
            .ToListAsync(ct);

        var items = groups.Select(g => new FailedMessageGroup(g.FailureCode, g.Count)).ToList();

        return new FailedMessagesSummary(count, items);
    }

    private async Task<ChannelIssuesSummary> GetChannelIssuesAsync(ClaimsPrincipal principal, DateTimeOffset now, CancellationToken ct)
    {
        var expiringThreshold = now.AddDays(CredentialsExpiringSoonDays);
        var (channelQuery, _) = await channelAccess.ApplyChannelAccessFilterAsync(db.Channels.AsNoTracking(), principal, ct);

        // is_active қасдан ин ҷо НЕСТ (2026-08-26, А2) — канали қасдан ғайрифаъолшуда мушкил нест.
        var query = channelQuery.Where(c =>
            c.RequiresReconnect || c.WebhookSetupWarning != null ||
            (c.CredentialsExpiresAt != null && c.CredentialsExpiresAt <= expiringThreshold));

        var count = await query.CountAsync(ct);
        var rows = await query
            .OrderBy(c => c.Name)
            .Take(PreviewLimit)
            .Select(c => new { c.Id, c.Name, c.RequiresReconnect, c.WebhookSetupWarning, c.CredentialsExpiresAt })
            .ToListAsync(ct);

        // Матни сабаб (якчанд шарт метавонад якҷоя рост бошад) ин ҷо, на дар SQL — танҳо барои
        // ҳадди ниҳоят 5 сатр (PreviewLimit), на ҳамаи канал; COUNT-и воқеӣ аллакай дар SQL боло.
        var items = rows
            .Select(c => new ChannelIssueItem(
                c.Id, c.Name,
                // WHERE-и боло аллакай тасдиқ кард, ки ҳадди ақал як шарт рост аст.
                ChannelIssueReasonResolver.Resolve(
                    c.RequiresReconnect, c.WebhookSetupWarning, c.CredentialsExpiresAt, now, TimeSpan.FromDays(CredentialsExpiringSoonDays))
                    ?? string.Empty))
            .ToList();

        return new ChannelIssuesSummary(count, items);
    }

    private async Task<TaskGroupSummary> GetOverdueTasksAsync(ClaimsPrincipal principal, DateOnly today, CancellationToken ct)
    {
        var query = db.Tasks.AsNoTracking()
            .Where(t => t.DueDate != null && t.DueDate < today && !t.Column.IsDoneColumn);

        // Вазифаҳо ба Project тааллуқ доранд, на ба Channel — ҳамон филтри TasksEndpoints.ListAsync.
        if (!ProjectAccessGuard.CanSeeAllProjects(principal))
        {
            var userId = principal.GetUserId();
            query = query.Where(t => t.Project.Members.Any(m => m.UserId == userId));
        }

        var count = await query.CountAsync(ct);
        var items = await query
            .OrderBy(t => t.DueDate)
            .ThenBy(t => t.Title)
            .Take(PreviewLimit)
            .Select(t => new TaskPreviewItem(t.Id, t.Title, t.Project.Name, t.ProjectId))
            .ToListAsync(ct);

        return new TaskGroupSummary(count, items);
    }

    // ============================= Блоки 2: myWork (ҳамеша шахсӣ) =============================

    private async Task<MyWorkDto> GetMyWorkAsync(
        ClaimsPrincipal principal, IQueryable<Conversation> accessibleConversations, DateOnly today, CancellationToken ct)
    {
        var userId = principal.GetUserId();

        // Ҳамон accessibleConversations-и Блоки 1 (боло, як бор ҳисобшуда) — на дархости нав.
        // Ин маҳз ҳамон ҷое, ки "муколамаи ба ман вогузошташуда, вале дар канале ки ман
        // дастрасӣ надорам" худкор канда мешавад: ApplyAccessFilterAsync аллакай ин ҳолатро
        // (агар мавҷуд бошад — ниг. тест) аз рӯйхат хориҷ кардааст, пеш аз он ки AssignedTo
        // санҷида шавад.
        var myConversationsQuery = accessibleConversations.Where(c => c.AssignedTo == userId);

        // Гурӯҳ+ҷамъи хонданашуда дар ЯК дархост — на ду (як барои гурӯҳҳо, дигаре барои SUM).
        // Ҷамъбасти ниҳоии MyUnread дар C# аст, вале бар рӯи натиҷаи АЛЛАКАЙ хурди SQL (то 4
        // сатр — шумораи ConversationStatus), на бар рӯи паёмҳо/муколамаҳои хом.
        var statusRows = await myConversationsQuery
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Unread = g.Sum(c => c.UnreadCount) })
            .ToListAsync(ct);

        var myConversations = statusRows.Select(r => new ConversationStatusGroup(r.Status.ToString(), r.Count)).ToList();
        var myUnread = statusRows.Sum(r => r.Unread);

        var myTasksToday = await GetMyTaskGroupAsync(principal, userId, t => t.DueDate == today, ct);
        var myTasksOverdue = await GetMyTaskGroupAsync(principal, userId, t => t.DueDate < today, ct);

        return new MyWorkDto(myConversations, myUnread, myTasksToday, myTasksOverdue);
    }

    private async Task<TaskGroupSummary> GetMyTaskGroupAsync(
        ClaimsPrincipal principal, Guid userId, Expression<Func<TaskItem, bool>> dueDatePredicate, CancellationToken ct)
    {
        // Ҳамон таърифи "сутуни хотимавӣ", ки overdueTasks-и Блоки 1 истифода мебарад
        // (BoardColumn.IsDoneColumn) — ду таърифи гуногун набояд бошад.
        var query = db.Tasks.AsNoTracking()
            .Where(t => t.AssigneeId == userId && t.DueDate != null && !t.Column.IsDoneColumn)
            .Where(dueDatePredicate);

        // Ҳамон филтри overdueTasks-и Блоки 1 (TasksEndpoints.ListAsync) — эҳтиёт барои ҳолати
        // нодир: таъин шудам, баъд аз project бароварда шудам, вазифа боқӣ монд.
        if (!ProjectAccessGuard.CanSeeAllProjects(principal))
            query = query.Where(t => t.Project.Members.Any(m => m.UserId == userId));

        var count = await query.CountAsync(ct);
        var items = await query
            .OrderBy(t => t.DueDate)
            .ThenBy(t => t.Title)
            .Take(PreviewLimit)
            .Select(t => new TaskPreviewItem(t.Id, t.Title, t.Project.Name, t.ProjectId))
            .ToListAsync(ct);

        return new TaskGroupSummary(count, items);
    }
}
