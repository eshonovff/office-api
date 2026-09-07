namespace Office.Api.Features.Dashboard;

// CanSeeStats — ҳамон ChannelAccessGuard.CanSeeAllChannels; фронт то ин ҷо isOwnerOrAdmin(roles)-ро
// худаш тафсир мекард (нусхаи мантиқи бэкенд) — акнун танҳо ин байрақро мехонад.
public record DashboardResponse(ActionRequiredDto ActionRequired, MyWorkDto MyWork, bool CanSeeStats);

public record ActionRequiredDto(
    ClosingWindowsSummary ClosingWindows,
    int Unassigned,
    FailedMessagesSummary FailedMessages,
    ChannelIssuesSummary ChannelIssues,
    TaskGroupSummary OverdueTasks);

public record ClosingWindowItem(Guid ConversationId, string ContactLabel, DateTimeOffset WindowExpiresAt);

public record ClosingWindowsSummary(int Count, IReadOnlyList<ClosingWindowItem> Items);

// Гурӯҳбандӣ аз рӯи FailureCode, на рӯйхати паёмҳои алоҳида — fbtrace_id (дар FailureDetail)
// дар ҳар дархост ягона аст, GROUP BY бар он бефоида буд (20 сатр = 20 гурӯҳи "беном").
// Қасдан бе матни тайёр — фронт бисёрзабона аст (tg/ru), тарҷума дар он ҷо мешавад
// (FailureCodeLabels барои логи backend мемонад, ба API намеравад).
public record FailedMessageGroup(string? FailureCode, int Count);

public record FailedMessagesSummary(int Count, IReadOnlyList<FailedMessageGroup> Groups);

public record ChannelIssueItem(Guid ChannelId, string ChannelName, string Reason);

public record ChannelIssuesSummary(int Count, IReadOnlyList<ChannelIssueItem> Items);

// Блоки 2 — ҳамеша шахсӣ (ҳатто барои Owner: "кори ман", на кори тим).

public record ConversationStatusGroup(string Status, int Count);

// ProjectId — фронт бе он линки воқеӣ ба тахтаи дуруст (/projects/{id}?task={taskId}) сохта
// наметавонист (танҳо ProjectName набуд кофӣ). Query аллакай ба Project join мекунад (барои
// ProjectName), пас илова кардани ин майдон дархости иловагӣ намеорад — танҳо як select бештар.
public record TaskPreviewItem(Guid Id, string Title, string ProjectName, Guid ProjectId);

public record TaskGroupSummary(int Count, IReadOnlyList<TaskPreviewItem> Items);

public record MyWorkDto(
    IReadOnlyList<ConversationStatusGroup> MyConversations,
    int MyUnread,
    TaskGroupSummary MyTasksToday,
    TaskGroupSummary MyTasksOverdue);
