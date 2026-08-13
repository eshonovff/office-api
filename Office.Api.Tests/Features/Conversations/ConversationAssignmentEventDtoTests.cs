using Office.Api.Data.Entities;
using Office.Api.Features.Conversations;

namespace Office.Api.Tests.Features.Conversations;

public class ConversationAssignmentEventDtoTests
{
    private static ConversationAssignmentEvent CreateEvent() => new()
    {
        Id = Guid.NewGuid(),
        ConversationId = Guid.NewGuid(),
        Reason = ConversationAssignmentReason.Reassigned,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void FromEntity_MapsReasonAsString()
    {
        var e = CreateEvent();
        e.Reason = ConversationAssignmentReason.Takeover;

        var dto = ConversationAssignmentEventDto.FromEntity(e);

        Assert.Equal("Takeover", dto.Reason);
    }

    [Theory]
    [InlineData(ConversationAssignmentReason.ClaimedOnReply, "ClaimedOnReply")]
    [InlineData(ConversationAssignmentReason.Takeover, "Takeover")]
    [InlineData(ConversationAssignmentReason.Reassigned, "Reassigned")]
    [InlineData(ConversationAssignmentReason.AutoReleased, "AutoReleased")]
    public void FromEntity_MapsEveryReasonVariant(ConversationAssignmentReason reason, string expected)
    {
        var e = CreateEvent();
        e.Reason = reason;

        var dto = ConversationAssignmentEventDto.FromEntity(e);

        Assert.Equal(expected, dto.Reason);
    }

    [Fact]
    public void FromEntity_ClaimedFromUnassigned_FromUserIsNull()
    {
        // ClaimedOnReply — чат пеш аз ин таъиннашуда буд, пас FromUserId/FromUserName ҳарду null.
        var e = CreateEvent();
        e.Reason = ConversationAssignmentReason.ClaimedOnReply;
        e.FromUserId = null;
        e.FromUserName = null;
        e.ToUserId = Guid.NewGuid();
        e.ToUserName = "Operator";

        var dto = ConversationAssignmentEventDto.FromEntity(e);

        Assert.Null(dto.FromUserId);
        Assert.Null(dto.FromUserName);
        Assert.Equal("Operator", dto.ToUserName);
    }

    [Fact]
    public void FromEntity_ReadsTheStoredSnapshotNotANavigation()
    {
        // FromUserName/ToUserName — snapshot дар лаҳзаи таъин, на navigation (энтитӣ ин
        // навигатсияро тамоман надорад — FK-и FromUserId/ToUserId навигатсия намесозад).
        var e = CreateEvent();
        e.FromUserId = Guid.NewGuid();
        e.FromUserName = "Former Assignee";
        e.ToUserId = Guid.NewGuid();
        e.ToUserName = "New Assignee";

        var dto = ConversationAssignmentEventDto.FromEntity(e);

        Assert.Equal("Former Assignee", dto.FromUserName);
        Assert.Equal("New Assignee", dto.ToUserName);
    }

    [Fact]
    public void FromEntity_AssigneeDeleted_SnapshotSurvivesEvenThoughTheFkWentNull()
    {
        // FromUserId/ToUserId → SET NULL ҳангоми нест кардани корбар (ниг.
        // ConversationAssignmentEventConfiguration), вале FromUserName/ToUserName
        // (snapshot-и мустақил) мемонанд — ин тамоми сабаби вуҷуди ин ду колонка аст.
        var e = CreateEvent();
        e.FromUserId = null;
        e.FromUserName = "Former Operator";
        e.ToUserId = null;
        e.ToUserName = "Deleted Manager";

        var dto = ConversationAssignmentEventDto.FromEntity(e);

        Assert.Null(dto.FromUserId);
        Assert.Equal("Former Operator", dto.FromUserName);
        Assert.Null(dto.ToUserId);
        Assert.Equal("Deleted Manager", dto.ToUserName);
    }

    [Fact]
    public void FromEntity_MapsIdAndCreatedAt()
    {
        var e = CreateEvent();

        var dto = ConversationAssignmentEventDto.FromEntity(e);

        Assert.Equal(e.Id, dto.Id);
        Assert.Equal(e.CreatedAt, dto.CreatedAt);
    }
}
