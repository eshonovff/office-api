using Office.Api.Channels;
using Office.Api.Data.Entities;

namespace Office.Api.Tests.Channels;

public class ConversationWindowCalculatorTests
{
    private static ParsedWebhookMessage Message(MessageDirection direction, DateTimeOffset sentAt) => new(
        ConversationExternalId: "conv-1",
        ContactName: null,
        ContactAvatarUrl: null,
        MessageExternalId: Guid.NewGuid().ToString(),
        Direction: direction,
        Type: MessageType.Text,
        Body: "hi",
        MediaUrl: null,
        SentAt: sentAt);

    [Fact]
    public void ComputeExpiresAt_InboundMessage_ExtendsWindowBy24Hours()
    {
        var sentAt = new DateTimeOffset(2026, 8, 7, 10, 0, 0, TimeSpan.Zero);
        var messages = new[] { Message(MessageDirection.Inbound, sentAt) };

        var result = ConversationWindowCalculator.ComputeExpiresAt(messages, currentExpiresAt: null);

        Assert.Equal(sentAt.AddHours(24), result);
    }

    [Fact]
    public void ComputeExpiresAt_OutboundOnly_DoesNotChangeExistingWindow()
    {
        var existing = new DateTimeOffset(2026, 8, 7, 10, 0, 0, TimeSpan.Zero);
        var messages = new[] { Message(MessageDirection.Outbound, existing.AddHours(1)) };

        var result = ConversationWindowCalculator.ComputeExpiresAt(messages, currentExpiresAt: existing);

        Assert.Equal(existing, result);
    }

    [Fact]
    public void ComputeExpiresAt_OutboundOnly_NoExistingWindow_StaysNull()
    {
        var messages = new[] { Message(MessageDirection.Outbound, DateTimeOffset.UtcNow) };

        var result = ConversationWindowCalculator.ComputeExpiresAt(messages, currentExpiresAt: null);

        Assert.Null(result);
    }

    [Fact]
    public void ComputeExpiresAt_MixedBatch_UsesLatestInboundOnly()
    {
        var earlierInbound = new DateTimeOffset(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);
        var laterInbound = new DateTimeOffset(2026, 8, 7, 11, 0, 0, TimeSpan.Zero);
        var laterOutbound = new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

        var messages = new[]
        {
            Message(MessageDirection.Inbound, earlierInbound),
            Message(MessageDirection.Outbound, laterOutbound),
            Message(MessageDirection.Inbound, laterInbound),
        };

        var result = ConversationWindowCalculator.ComputeExpiresAt(messages, currentExpiresAt: null);

        Assert.Equal(laterInbound.AddHours(24), result);
    }

    private static readonly DateTimeOffset Now = new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IsWindowClosed_NoExpiryEverSet_TextMessage_ReturnsFalse()
    {
        // Ҳеҷ гоҳ паёми воридотӣ наомадааст — ин ба провайдер меафтад, ки худаш хатои
        // воқеиро медиҳад (реактивӣ), на ба ин санҷиши пешакӣ.
        Assert.False(ConversationWindowCalculator.IsWindowClosed(isTemplate: false, windowExpiresAt: null, Now));
    }

    [Fact]
    public void IsWindowClosed_StillOpen_ReturnsFalse()
    {
        Assert.False(ConversationWindowCalculator.IsWindowClosed(isTemplate: false, windowExpiresAt: Now.AddHours(1), Now));
    }

    [Fact]
    public void IsWindowClosed_AlreadyExpired_TextMessage_ReturnsTrue()
    {
        Assert.True(ConversationWindowCalculator.IsWindowClosed(isTemplate: false, windowExpiresAt: Now.AddHours(-1), Now));
    }

    [Fact]
    public void IsWindowClosed_ExactlyAtExpiry_ReturnsTrue()
    {
        Assert.True(ConversationWindowCalculator.IsWindowClosed(isTemplate: false, windowExpiresAt: Now, Now));
    }

    [Fact]
    public void IsWindowClosed_Template_IgnoresExpiredWindow_ReturnsFalse()
    {
        // Шаблон новобаста аз тиреза кор мекунад — маҳз барои ҳамин мавҷуд аст.
        Assert.False(ConversationWindowCalculator.IsWindowClosed(isTemplate: true, windowExpiresAt: Now.AddHours(-1), Now));
    }
}
