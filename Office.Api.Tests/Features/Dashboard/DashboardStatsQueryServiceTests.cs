using Microsoft.EntityFrameworkCore;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Features.Dashboard;

namespace Office.Api.Tests.Features.Dashboard;

public class DashboardStatsQueryServiceTests
{
    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Channel SeedChannel(AppDbContext db, ChannelType type = ChannelType.WhatsApp, bool isActive = true)
    {
        var channel = new Channel
        {
            Id = Guid.CreateVersion7(), Type = type, Name = $"Channel-{Guid.NewGuid():N}",
            ExternalId = Guid.NewGuid().ToString(), IsActive = isActive, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Channels.Add(channel);
        return channel;
    }

    private static Conversation SeedConversation(
        AppDbContext db, Channel channel, ConversationStatus status = ConversationStatus.InProgress, Guid? assignedTo = null)
    {
        var conversation = new Conversation
        {
            Id = Guid.CreateVersion7(), ChannelId = channel.Id, Channel = channel,
            ExternalId = Guid.NewGuid().ToString(), Status = status, AssignedTo = assignedTo,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Conversations.Add(conversation);
        return conversation;
    }

    private static Message SeedMessage(
        AppDbContext db, Conversation conversation, MessageDirection direction, DateTimeOffset createdAt,
        MessageDeliveryStatus status = MessageDeliveryStatus.Delivered, string? failureCode = null)
    {
        var message = new Message
        {
            Id = Guid.CreateVersion7(), ConversationId = conversation.Id, Direction = direction,
            Type = MessageType.Text, DeliveryStatus = status, FailureCode = failureCode, CreatedAt = createdAt,
        };
        db.Messages.Add(message);
        return message;
    }

    [Fact]
    public async Task ComputeAsync_EmptyDatabase_ReturnsZeroStructureNotException()
    {
        var db = CreateDb();
        var service = new DashboardStatsQueryService(db);

        var result = await service.ComputeAsync(14, CancellationToken.None);

        Assert.Equal(14, result.VolumeByDay.Data.Count); // 14 рӯз, ҳама сифр — на партофта
        Assert.All(result.VolumeByDay.Data, d => Assert.Equal(0, d.Inbound + d.Outbound));
        Assert.False(result.VolumeByDay.Sufficient);

        Assert.Empty(result.ByChannel.Data);
        Assert.False(result.ByChannel.Sufficient);

        Assert.Empty(result.OperatorLoad.Data);
        Assert.False(result.OperatorLoad.Sufficient);

        Assert.Empty(result.HourlyHeatmap.Data);
        Assert.False(result.HourlyHeatmap.Sufficient);

        Assert.Equal(0, result.ResponseTimeBuckets.Unanswered);
        Assert.All(result.ResponseTimeBuckets.Data, b => Assert.Equal(0, b.Count));
        Assert.False(result.ResponseTimeBuckets.Sufficient);

        Assert.Empty(result.MessageStatus.Data);
        Assert.False(result.MessageStatus.Sufficient);

        Assert.Empty(result.FailureBreakdown.Data);
        Assert.False(result.FailureBreakdown.Sufficient);

        Assert.All(result.Funnel.Data, s => Assert.Equal(0, s.Count));
        Assert.False(result.Funnel.Sufficient);
    }

    [Fact]
    public async Task ComputeAsync_VolumeByDay_LocalMidnightBoundary_PutsMessageInCorrectLocalDay()
    {
        var db = CreateDb();
        var channel = SeedChannel(db);
        var conversation = SeedConversation(db, channel);

        // ComputeAsync худаш DateTimeOffset.UtcNow мегирад — марзро нисбат ба "ҳозир" месозем
        // (на санаи собит), то ҳамеша дар давраи ҳисобшуда бошад, новобаста аз рӯзи иҷрои тест.
        var now = DateTimeOffset.UtcNow;
        var localToday = OfficeLocalDate.Today(now);
        var localMidnightUtc = new DateTimeOffset(localToday.Year, localToday.Month, localToday.Day, 0, 0, 0, TimeSpan.Zero)
            .AddHours(-OfficeLocalDate.OfficeUtcOffsetHours);

        SeedMessage(db, conversation, MessageDirection.Inbound, localMidnightUtc.AddSeconds(-1)); // дирӯзи маҳаллӣ
        SeedMessage(db, conversation, MessageDirection.Inbound, localMidnightUtc); // имрӯзи маҳаллӣ, марз худаш

        await db.SaveChangesAsync();

        var service = new DashboardStatsQueryService(db);
        var result = await service.ComputeAsync(14, CancellationToken.None);

        var yesterday = result.VolumeByDay.Data.SingleOrDefault(d => d.Date == localToday.AddDays(-1));
        var today = result.VolumeByDay.Data.SingleOrDefault(d => d.Date == localToday);

        Assert.NotNull(yesterday);
        Assert.Equal(1, yesterday.Inbound);
        Assert.NotNull(today);
        Assert.Equal(1, today.Inbound);
    }

    [Fact]
    public async Task ComputeAsync_HourlyHeatmap_UsesLocalTimeNotUtc()
    {
        var db = CreateDb();
        var channel = SeedChannel(db);
        var conversation = SeedConversation(db, channel);

        var messageAt = DateTimeOffset.UtcNow.AddHours(-3);
        SeedMessage(db, conversation, MessageDirection.Inbound, messageAt);

        await db.SaveChangesAsync();

        var service = new DashboardStatsQueryService(db);
        var result = await service.ComputeAsync(14, CancellationToken.None);

        // Интизорӣ мустақилона аз ҳамон OfficeLocalDate ҳисоб мешавад (на UTC-и хом
        // messageAt.Hour/.DayOfWeek) — то воқеан тарҷумаи SQL-и query-ро санҷем, на танҳо
        // такрор кунем.
        var point = Assert.Single(result.HourlyHeatmap.Data);
        Assert.Equal(OfficeLocalDate.LocalHour(messageAt), point.Hour);
        Assert.Equal((int)OfficeLocalDate.LocalDayOfWeek(messageAt), point.DayOfWeek);
    }

    [Fact]
    public async Task ComputeAsync_ResponseTimeBuckets_ConversationWithNoOutbound_CountsAsUnanswered()
    {
        var db = CreateDb();
        var channel = SeedChannel(db);
        var conversation = SeedConversation(db, channel);
        SeedMessage(db, conversation, MessageDirection.Inbound, DateTimeOffset.UtcNow.AddHours(-2));

        await db.SaveChangesAsync();

        var service = new DashboardStatsQueryService(db);
        var result = await service.ComputeAsync(14, CancellationToken.None);

        Assert.Equal(1, result.ResponseTimeBuckets.Unanswered);
        Assert.All(result.ResponseTimeBuckets.Data, b => Assert.Equal(0, b.Count));
    }

    [Fact]
    public async Task ComputeAsync_ResponseTimeBuckets_OutboundBeforeInbound_IsCorruptDataAndCountsAsUnansweredNeverNegative()
    {
        // Мушаххас: муколамае, ки "ҷавоби аввалаш" пеш аз паёми воридотӣ аст (маълумоти вайрон,
        // масалан агенте бидуни мижоз паёме навишт). Набояд бакети манфӣ диҳад — бояд бе ҷавоб
        // ҳисоб шавад, чунки ягон содиротии БАЪДИ воридотӣ нест.
        var db = CreateDb();
        var channel = SeedChannel(db);
        var conversation = SeedConversation(db, channel);
        var inboundAt = DateTimeOffset.UtcNow.AddHours(-1);
        SeedMessage(db, conversation, MessageDirection.Outbound, inboundAt.AddHours(-3)); // пеш аз воридотӣ
        SeedMessage(db, conversation, MessageDirection.Inbound, inboundAt);

        await db.SaveChangesAsync();

        var service = new DashboardStatsQueryService(db);
        var result = await service.ComputeAsync(14, CancellationToken.None);

        Assert.Equal(1, result.ResponseTimeBuckets.Unanswered);
        Assert.All(result.ResponseTimeBuckets.Data, b => Assert.Equal(0, b.Count));
    }

    [Fact]
    public async Task ComputeAsync_ResponseTimeBuckets_RespondedConversation_BucketsTheRealResponseAfterInbound()
    {
        var db = CreateDb();
        var channel = SeedChannel(db);
        var conversation = SeedConversation(db, channel);
        var inboundAt = DateTimeOffset.UtcNow.AddHours(-2);
        SeedMessage(db, conversation, MessageDirection.Inbound, inboundAt);
        SeedMessage(db, conversation, MessageDirection.Outbound, inboundAt.AddMinutes(3)); // < 5 мин

        await db.SaveChangesAsync();

        var service = new DashboardStatsQueryService(db);
        var result = await service.ComputeAsync(14, CancellationToken.None);

        Assert.Equal(0, result.ResponseTimeBuckets.Unanswered);
        var under5 = result.ResponseTimeBuckets.Data.Single(b => b.Bucket == ResponseTimeBucketer.Under5Min);
        Assert.Equal(1, under5.Count);
    }

    [Fact]
    public async Task ComputeAsync_Funnel_ReflectsArrivedRespondedAndClosedFromExistingStatus()
    {
        var db = CreateDb();
        var channel = SeedChannel(db);

        var closedAndResponded = SeedConversation(db, channel, status: ConversationStatus.Closed);
        var inboundAt1 = DateTimeOffset.UtcNow.AddHours(-3);
        SeedMessage(db, closedAndResponded, MessageDirection.Inbound, inboundAt1);
        SeedMessage(db, closedAndResponded, MessageDirection.Outbound, inboundAt1.AddMinutes(10));

        var openUnanswered = SeedConversation(db, channel, status: ConversationStatus.New);
        SeedMessage(db, openUnanswered, MessageDirection.Inbound, DateTimeOffset.UtcNow.AddHours(-1));

        await db.SaveChangesAsync();

        var service = new DashboardStatsQueryService(db);
        var result = await service.ComputeAsync(14, CancellationToken.None);

        Assert.Equal(2, result.Funnel.Data.Single(s => s.Stage == "Омад").Count);
        Assert.Equal(1, result.Funnel.Data.Single(s => s.Stage == "Ҷавоб гирифт").Count);
        Assert.Equal(1, result.Funnel.Data.Single(s => s.Stage == "Баста шуд").Count);
    }

    [Fact]
    public async Task ComputeAsync_ByChannel_ExcludesInactiveChannelAndClosedConversations()
    {
        var db = CreateDb();
        var activeChannel = SeedChannel(db);
        var inactiveChannel = SeedChannel(db, isActive: false);

        SeedConversation(db, activeChannel, status: ConversationStatus.InProgress);
        SeedConversation(db, activeChannel, status: ConversationStatus.Closed); // баста — набояд ба ҳисоб равад
        SeedConversation(db, inactiveChannel, status: ConversationStatus.InProgress); // канали ғайрифаъол — набояд ба ҳисоб равад

        await db.SaveChangesAsync();

        var service = new DashboardStatsQueryService(db);
        var result = await service.ComputeAsync(14, CancellationToken.None);

        var point = Assert.Single(result.ByChannel.Data);
        Assert.Equal(activeChannel.Id, point.ChannelId);
        Assert.Equal(1, point.ActiveConversations);
    }

    [Fact]
    public async Task ComputeAsync_FailureBreakdown_GroupsByFailureCodeWithinTheSelectedWindow()
    {
        var db = CreateDb();
        var channel = SeedChannel(db);
        var conversation = SeedConversation(db, channel);

        SeedMessage(db, conversation, MessageDirection.Outbound, DateTimeOffset.UtcNow.AddDays(-1),
            status: MessageDeliveryStatus.Failed, failureCode: "IG_2");
        SeedMessage(db, conversation, MessageDirection.Outbound, DateTimeOffset.UtcNow.AddDays(-1),
            status: MessageDeliveryStatus.Failed, failureCode: "IG_2");
        SeedMessage(db, conversation, MessageDirection.Outbound, DateTimeOffset.UtcNow.AddDays(-20), // берун аз 14 рӯз
            status: MessageDeliveryStatus.Failed, failureCode: "IG_2");

        await db.SaveChangesAsync();

        var service = new DashboardStatsQueryService(db);
        var result = await service.ComputeAsync(14, CancellationToken.None);

        var group = Assert.Single(result.FailureBreakdown.Data);
        Assert.Equal("IG_2", group.FailureCode);
        Assert.Equal(2, group.Count);
    }
}
