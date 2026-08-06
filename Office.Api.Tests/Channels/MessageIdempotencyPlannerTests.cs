using Office.Api.Channels;
using Office.Api.Data.Entities;

namespace Office.Api.Tests.Channels;

public class MessageIdempotencyPlannerTests
{
    private static ParsedWebhookMessage Message(string externalId) => new(
        ConversationExternalId: "contact-1",
        ContactName: "Test",
        ContactAvatarUrl: null,
        MessageExternalId: externalId,
        Direction: MessageDirection.Inbound,
        Type: MessageType.Text,
        Body: "salom",
        MediaUrl: null,
        SentAt: DateTimeOffset.UtcNow);

    [Fact]
    public void FilterNew_NoExisting_ReturnsAllIncoming()
    {
        var incoming = new[] { Message("a"), Message("b") };

        var result = MessageIdempotencyPlanner.FilterNew(incoming, new HashSet<string>());

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void FilterNew_AlreadyInDatabase_IsSkipped()
    {
        var incoming = new[] { Message("a"), Message("b") };
        var existing = new HashSet<string> { "a" };

        var result = MessageIdempotencyPlanner.FilterNew(incoming, existing);

        Assert.Single(result);
        Assert.Equal("b", result[0].MessageExternalId);
    }

    [Fact]
    public void FilterNew_SameWebhookDeliveredTwice_SecondCallReturnsEmpty()
    {
        var incoming = new[] { Message("a"), Message("b") };

        // Гирифти якум: ҳарду нав.
        var firstPass = MessageIdempotencyPlanner.FilterNew(incoming, new HashSet<string>());
        Assert.Equal(2, firstPass.Count);

        // Ҳамон webhook дубора омад — ҳоло "a" ва "b" аллакай дар база.
        var existingAfterFirstPass = firstPass.Select(m => m.MessageExternalId).ToHashSet();
        var secondPass = MessageIdempotencyPlanner.FilterNew(incoming, existingAfterFirstPass);

        Assert.Empty(secondPass);
    }

    [Fact]
    public void FilterNew_DuplicateWithinSameBatch_OnlyFirstOccurrenceKept()
    {
        var incoming = new[] { Message("a"), Message("a") };

        var result = MessageIdempotencyPlanner.FilterNew(incoming, new HashSet<string>());

        Assert.Single(result);
    }
}
