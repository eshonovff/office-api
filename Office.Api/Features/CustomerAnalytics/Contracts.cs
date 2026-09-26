namespace Office.Api.Features.CustomerAnalytics;

/// <summary>A number in the chosen period and in the same number of days just before it.</summary>
public record AnalyticsCount(int Current, int Previous);

public record AnalyticsDay(DateOnly Date, int NewContacts, int InboundMessages, int AutomaticReplies);

public record AnalyticsBroadcasts(int Broadcasts, int Sent, int Skipped, int Failed);

/// <param name="AutoReplied">Of those comments, how many the comment auto-reply answered in public.</param>
public record AnalyticsTopPost(Guid ChannelId, string MediaId, int Comments, int AutoReplied);

/// <param name="Converted">
/// Of the people who started an automation in the period, those who reached its "Конверсия" step
/// (at any time) — so a rate is never above 100 %.
/// </param>
public record AnalyticsOverview(
    DateOnly From,
    DateOnly To,
    AnalyticsCount NewContacts,
    AnalyticsCount InboundMessages,
    AnalyticsCount AutomaticReplies,
    AnalyticsCount ManualReplies,
    AnalyticsCount Comments,
    AnalyticsCount Started,
    AnalyticsCount Converted,
    IReadOnlyList<AnalyticsDay> Days,
    AnalyticsBroadcasts Broadcasts,
    IReadOnlyList<AnalyticsTopPost> TopPosts);

/// <param name="HasConversionStep">False — the flow has no "Конверсия" step, so there is no goal to count.</param>
public record AnalyticsFlowRow(
    Guid FlowId, Guid ChannelId, string Name, bool IsActive, bool HasConversionStep, AnalyticsCount Started, AnalyticsCount Converted);

/// <param name="AskedToFollow">People told "follow, then comment again".</param>
/// <param name="FollowedAfterAsking">Of them, those found following on a later comment.</param>
public record AnalyticsRuleRow(
    Guid RuleId, Guid ChannelId, string Name, bool IsActive,
    int Comments, int PublicReplies, int DirectMessages, int Errors, int AskedToFollow, int FollowedAfterAsking);

public record AnalyticsAutomations(DateOnly From, DateOnly To, IReadOnlyList<AnalyticsFlowRow> Flows, IReadOnlyList<AnalyticsRuleRow> Rules);

public record AnalyticsFlowDay(DateOnly Date, int Started, int Converted);

public record AnalyticsStarter(Guid ContactId, string? Name, string? Username, DateTimeOffset StartedAt, bool Converted);

public record AnalyticsFlowDetail(
    Guid FlowId, string Name, bool HasConversionStep, DateOnly From, DateOnly To,
    AnalyticsCount Started, AnalyticsCount Converted, IReadOnlyList<AnalyticsFlowDay> Days, IReadOnlyList<AnalyticsStarter> RecentStarters);
