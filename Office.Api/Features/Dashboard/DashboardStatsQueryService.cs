using Microsoft.EntityFrameworkCore;
using Office.Api.Data;
using Office.Api.Data.Entities;

namespace Office.Api.Features.Dashboard;

/// <summary>
/// GET /api/dashboard/stats (Блоки 3) — тим-вокеъ, бе филтри дастрасии шахсӣ: endpoint худаш
/// Owner/Admin-ро талаб мекунад (RequireOwnerOrAdmin — ҳамон ChannelAccessGuard.CanSeeAllChannels),
/// пас ҳар кӣ ин методро даъват карда метавонад аллакай ҳама чизро мебинад — ApplyAccessFilterAsync
/// лозим нест.
///
/// Ҳашт диаграмма — вале НЕ ҳашт round-trip: byChannel+operatorLoad аз ЯК дархост (ҳарду аз
/// муколамаҳои кушода), responseTimeBuckets+funnel аз ЯК дархост (ҳарду аз "аввалин ҷавоб").
/// </summary>
public class DashboardStatsQueryService(AppDbContext db)
{
    // Ҳадди "маълумот кофӣ" — сеашон аз спецификатсия (7/20/100), боқимонда интихоби худам
    // (ҳуҷҷатнок карда шуд дар report/phase doc): ҳадафи ҳама — то диаграммаи "шакли тасодуфӣ"-и
    // бо маълумоти кам ҳамчун далел нишон дода нашавад.
    private const int VolumeByDaySufficientDays = 7;
    private const int ResponseTimeSufficientConversations = 20;
    private const int HourlyHeatmapSufficientMessages = 100;
    private const int ByChannelSufficientConversations = 5;
    private const int OperatorLoadSufficientConversations = 5;
    private const int MessageStatusSufficientMessages = 20;
    private const int FailureBreakdownSufficientMessages = 5;
    private const int FunnelSufficientConversations = 10;

    public async Task<DashboardStatsResponse> ComputeAsync(int days, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var since = now.AddDays(-days);
        var today = OfficeLocalDate.Today(now);

        var volumeByDay = await GetVolumeByDayAsync(since, today, days, ct);
        var (byChannel, operatorLoad) = await GetByChannelAndOperatorLoadAsync(ct);
        var hourlyHeatmap = await GetHourlyHeatmapAsync(since, ct);
        var (responseTimeBuckets, funnel) = await GetResponseTimeAndFunnelAsync(since, ct);
        var messageStatus = await GetMessageStatusAsync(since, ct);
        var failureBreakdown = await GetFailureBreakdownAsync(since, ct);

        return new DashboardStatsResponse(
            volumeByDay, byChannel, operatorLoad, hourlyHeatmap, responseTimeBuckets, messageStatus, failureBreakdown, funnel);
    }

    private async Task<DayVolumeResult> GetVolumeByDayAsync(DateTimeOffset since, DateOnly today, int days, CancellationToken ct)
    {
        // Табдили минтақаи вақт дар СЮЛ (AddHours(5)-и доимӣ) — на баъд аз кашидан ба хотира,
        // вагарна буриши рӯз бо соати UTC мешавад, на маҳаллӣ (ниг. дархости корбар).
        var rows = await db.Messages.AsNoTracking()
            .Where(m => m.CreatedAt >= since)
            .GroupBy(m => new { LocalDate = m.CreatedAt.AddHours(OfficeLocalDate.OfficeUtcOffsetHours).Date, m.Direction })
            .Select(g => new { g.Key.LocalDate, g.Key.Direction, Count = g.Count() })
            .ToListAsync(ct);

        // Аз ин ҷо ба поён — на дар СЮЛ: натиҷаи аллакай хурди GROUP BY (ҳадди аксар 2×days
        // сатр) бо рӯзҳои холӣ пур карда мешавад, на ҳамаи паём аз нав шумурда мешавад.
        var byDate = rows
            .GroupBy(r => DateOnly.FromDateTime(r.LocalDate))
            .ToDictionary(
                g => g.Key,
                g => (
                    Inbound: g.Where(r => r.Direction == MessageDirection.Inbound).Sum(r => r.Count),
                    Outbound: g.Where(r => r.Direction == MessageDirection.Outbound).Sum(r => r.Count)));

        // Рӯзҳои холӣ бо сифр пур мешаванд, на партофта мешаванд — вагарна график каҷ мешавад.
        var data = new List<DayVolumePoint>();
        for (var date = today.AddDays(-(days - 1)); date <= today; date = date.AddDays(1))
        {
            var (inbound, outbound) = byDate.GetValueOrDefault(date, (0, 0));
            data.Add(new DayVolumePoint(date, inbound, outbound));
        }

        var daysWithData = data.Count(d => d.Inbound > 0 || d.Outbound > 0);
        return new DayVolumeResult(data, daysWithData, daysWithData >= VolumeByDaySufficientDays);
    }

    private async Task<(ChannelVolumeResult ByChannel, OperatorLoadResult OperatorLoad)> GetByChannelAndOperatorLoadAsync(CancellationToken ct)
    {
        // "Фаъол" = кушода (Status != Closed, ҳамон таърифи Блоки 1) ва канали худаш is_active
        // (А2-и ҳамин рӯҳия: канали хомӯшкардашуда набояд дар статистика намоён бошад).
        var rows = await db.Conversations.AsNoTracking()
            .Where(c => c.Status != ConversationStatus.Closed && c.Channel.IsActive)
            .Select(c => new
            {
                c.ChannelId,
                ChannelName = c.Channel.Name,
                ChannelType = c.Channel.Type,
                c.AssignedTo,
                AssigneeName = c.Assignee != null ? c.Assignee.FullName : null,
            })
            .ToListAsync(ct);

        // Ду гурӯҳбандии гуногун бар рӯи ЯК натиҷаи аллакай маҳдуди SQL (муколамаҳои кушода —
        // боркунии зиндаи ширкат, на ҳаҷми таърихӣ) — на ду дархости алоҳида.
        var byChannel = rows
            .GroupBy(r => new { r.ChannelId, r.ChannelName, r.ChannelType })
            .Select(g => new ChannelVolumePoint(g.Key.ChannelId, g.Key.ChannelName, g.Key.ChannelType.ToString(), g.Count()))
            .OrderByDescending(p => p.ActiveConversations)
            .ToList();

        var operatorLoad = rows
            .Where(r => r.AssignedTo != null)
            .GroupBy(r => new { UserId = r.AssignedTo!.Value, r.AssigneeName })
            .Select(g => new OperatorLoadPoint(g.Key.UserId, g.Key.AssigneeName ?? "?", g.Count()))
            .OrderByDescending(p => p.OpenConversations)
            .ToList();

        var byChannelSampleSize = byChannel.Sum(p => p.ActiveConversations);
        var operatorLoadSampleSize = operatorLoad.Sum(p => p.OpenConversations);

        return (
            new ChannelVolumeResult(byChannel, byChannelSampleSize, byChannelSampleSize >= ByChannelSufficientConversations),
            new OperatorLoadResult(operatorLoad, operatorLoadSampleSize, operatorLoadSampleSize >= OperatorLoadSufficientConversations));
    }

    private async Task<HourlyHeatmapResult> GetHourlyHeatmapAsync(DateTimeOffset since, CancellationToken ct)
    {
        var rows = await db.Messages.AsNoTracking()
            .Where(m => m.Direction == MessageDirection.Inbound && m.CreatedAt >= since)
            .GroupBy(m => new
            {
                DayOfWeek = m.CreatedAt.AddHours(OfficeLocalDate.OfficeUtcOffsetHours).DayOfWeek,
                Hour = m.CreatedAt.AddHours(OfficeLocalDate.OfficeUtcOffsetHours).Hour,
            })
            .Select(g => new HeatmapPoint((int)g.Key.DayOfWeek, g.Key.Hour, g.Count()))
            .ToListAsync(ct);

        var sampleSize = rows.Sum(r => r.Count);
        return new HourlyHeatmapResult(rows, sampleSize, sampleSize >= HourlyHeatmapSufficientMessages);
    }

    private async Task<(ResponseTimeResult ResponseTimeBuckets, FunnelResult Funnel)> GetResponseTimeAndFunnelAsync(
        DateTimeOffset since, CancellationToken ct)
    {
        // Барои ҳар муколама (бо ақалан як воридотӣ дар давра): аввалин воридотӣ, ва аввалин
        // содиротии БАЪДИ он (на аввалин содиротии умуман) — маҳз ҳамин "БАЪДИ он" сохторан
        // дарозии манфиро (муколамае, ки ҷавобаш пеш аз воридотӣ омадааст) партофта мемонад,
        // на ба ҳисоб мегирад.
        var conversationTimes = await (
            from c in db.Conversations.AsNoTracking()
            let firstInbound = c.Messages
                .Where(m => m.Direction == MessageDirection.Inbound && m.CreatedAt >= since)
                .Min(m => (DateTimeOffset?)m.CreatedAt)
            where firstInbound != null
            let firstResponse = c.Messages
                .Where(m => m.Direction == MessageDirection.Outbound && m.CreatedAt > firstInbound)
                .Min(m => (DateTimeOffset?)m.CreatedAt)
            select new { c.Status, FirstInbound = firstInbound!.Value, FirstResponse = firstResponse }
        ).ToListAsync(ct);

        // Бакетбандӣ ва funnel дар C# — бар рӯи натиҷаи АЛЛАКАЙ маҳдуди SQL (муколамаҳои
        // воридотӣ дар давра, на ҳамаи паём).
        var bucketCounts = new Dictionary<string, int>();
        var unanswered = 0;
        var responded = 0;

        foreach (var row in conversationTimes)
        {
            if (row.FirstResponse is null)
            {
                unanswered++;
                continue;
            }

            responded++;
            var bucket = ResponseTimeBucketer.Bucket(row.FirstResponse.Value - row.FirstInbound);
            bucketCounts[bucket] = bucketCounts.GetValueOrDefault(bucket) + 1;
        }

        var bucketData = ResponseTimeBucketer.AllBuckets
            .Select(b => new ResponseTimeBucket(b, bucketCounts.GetValueOrDefault(b)))
            .ToList();
        var responseTimeResult = new ResponseTimeResult(
            bucketData, unanswered, responded, responded >= ResponseTimeSufficientConversations);

        var totalArrived = conversationTimes.Count;
        var closedCount = conversationTimes.Count(r => r.Status == ConversationStatus.Closed);
        var funnelData = new List<FunnelStage>
        {
            new("Омад", totalArrived),
            new("Ҷавоб гирифт", responded),
            new("Баста шуд", closedCount),
        };
        var funnelResult = new FunnelResult(funnelData, totalArrived, totalArrived >= FunnelSufficientConversations);

        return (responseTimeResult, funnelResult);
    }

    private async Task<MessageStatusResult> GetMessageStatusAsync(DateTimeOffset since, CancellationToken ct)
    {
        var rows = await db.Messages.AsNoTracking()
            .Where(m => m.CreatedAt >= since)
            .GroupBy(m => m.DeliveryStatus)
            .Select(g => new MessageStatusPoint(g.Key.ToString(), g.Count()))
            .ToListAsync(ct);

        var sampleSize = rows.Sum(r => r.Count);
        return new MessageStatusResult(rows, sampleSize, sampleSize >= MessageStatusSufficientMessages);
    }

    private async Task<FailureBreakdownResult> GetFailureBreakdownAsync(DateTimeOffset since, CancellationToken ct)
    {
        var rows = await db.Messages.AsNoTracking()
            .Where(m => m.DeliveryStatus == MessageDeliveryStatus.Failed && m.CreatedAt >= since)
            .GroupBy(m => m.FailureCode)
            .Select(g => new FailedMessageGroup(g.Key, g.Count()))
            .ToListAsync(ct);

        var data = rows.OrderByDescending(r => r.Count).ToList();
        var sampleSize = data.Sum(r => r.Count);
        return new FailureBreakdownResult(data, sampleSize, sampleSize >= FailureBreakdownSufficientMessages);
    }
}
