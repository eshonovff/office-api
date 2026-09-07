namespace Office.Api.Features.Dashboard;

public record DashboardStatsResponse(
    DayVolumeResult VolumeByDay,
    ChannelVolumeResult ByChannel,
    OperatorLoadResult OperatorLoad,
    HourlyHeatmapResult HourlyHeatmap,
    ResponseTimeResult ResponseTimeBuckets,
    MessageStatusResult MessageStatus,
    FailureBreakdownResult FailureBreakdown,
    FunnelResult Funnel);

public record DayVolumePoint(DateOnly Date, int Inbound, int Outbound);

public record DayVolumeResult(IReadOnlyList<DayVolumePoint> Data, int SampleSize, bool Sufficient);

public record ChannelVolumePoint(Guid ChannelId, string ChannelName, string ChannelType, int ActiveConversations);

public record ChannelVolumeResult(IReadOnlyList<ChannelVolumePoint> Data, int SampleSize, bool Sufficient);

public record OperatorLoadPoint(Guid UserId, string UserName, int OpenConversations);

public record OperatorLoadResult(IReadOnlyList<OperatorLoadPoint> Data, int SampleSize, bool Sufficient);

/// <summary>DayOfWeek — ҳамон рамзгузории .NET (Sunday=0 .. Saturday=6), на ISO (Monday=1).</summary>
public record HeatmapPoint(int DayOfWeek, int Hour, int Count);

public record HourlyHeatmapResult(IReadOnlyList<HeatmapPoint> Data, int SampleSize, bool Sufficient);

public record ResponseTimeBucket(string Bucket, int Count);

public record ResponseTimeResult(IReadOnlyList<ResponseTimeBucket> Data, int Unanswered, int SampleSize, bool Sufficient);

public record MessageStatusPoint(string Status, int Count);

public record MessageStatusResult(IReadOnlyList<MessageStatusPoint> Data, int SampleSize, bool Sufficient);

public record FailureBreakdownResult(IReadOnlyList<FailedMessageGroup> Data, int SampleSize, bool Sufficient);

public record FunnelStage(string Stage, int Count);

public record FunnelResult(IReadOnlyList<FunnelStage> Data, int SampleSize, bool Sufficient);
