using Office.Api.Data.Entities;
using Office.Api.Features.Conversations;

namespace Office.Api.Tests.Features.Conversations;

public class MessageDtoTests
{
    private static Message CreateMessage(Guid id) => new()
    {
        Id = id,
        ConversationId = Guid.NewGuid(),
        Direction = MessageDirection.Inbound,
        Type = MessageType.Image,
        DeliveryStatus = MessageDeliveryStatus.Delivered,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void FromEntity_MediaNotYetDownloaded_MediaUrlAndThumbnailUrlAreNull()
    {
        var message = CreateMessage(Guid.NewGuid());

        var dto = MessageDto.FromEntity(message);

        Assert.Null(dto.MediaUrl);
        Assert.Null(dto.ThumbnailUrl);
    }

    [Fact]
    public void FromEntity_MediaDownloaded_MediaUrlIsTheEndpointPathNotTheRawStoragePath()
    {
        var id = Guid.NewGuid();
        var message = CreateMessage(id);
        message.MediaUrl = "whatsapp-media/some-channel/019feb.jpg";

        var dto = MessageDto.FromEntity(message);

        Assert.Equal($"/api/messages/{id}/media", dto.MediaUrl);
        Assert.DoesNotContain("whatsapp-media", dto.MediaUrl);
    }

    [Fact]
    public void FromEntity_ThumbnailGenerated_ThumbnailUrlIsTheEndpointPath()
    {
        var id = Guid.NewGuid();
        var message = CreateMessage(id);
        message.ThumbnailUrl = "whatsapp-media/some-channel/019feb_thumb.jpg";

        var dto = MessageDto.FromEntity(message);

        Assert.Equal($"/api/messages/{id}/thumbnail", dto.ThumbnailUrl);
    }

    [Fact]
    public void FromEntity_DownloadFailed_CarriesTheErrorAndNoMediaUrl()
    {
        var message = CreateMessage(Guid.NewGuid());
        message.MediaDownloadError = "timed out after 5 retries";

        var dto = MessageDto.FromEntity(message);

        Assert.Equal("timed out after 5 retries", dto.MediaDownloadError);
        Assert.Null(dto.MediaUrl);
    }

    [Fact]
    public void FromEntity_MapsEnumsAndScalarFieldsAsStrings()
    {
        var message = CreateMessage(Guid.NewGuid());
        message.Type = MessageType.Audio;
        message.Direction = MessageDirection.Inbound;
        message.DeliveryStatus = MessageDeliveryStatus.Read;
        message.VoiceDurationSeconds = 7;

        var dto = MessageDto.FromEntity(message);

        Assert.Equal("Audio", dto.Type);
        Assert.Equal("Inbound", dto.Direction);
        Assert.Equal("Read", dto.DeliveryStatus);
        Assert.Equal(7, dto.VoiceDurationSeconds);
    }

    [Fact]
    public void FromEntity_InboundMessage_SentByUserNameIsNull()
    {
        // Паёми воридотӣ (аз мижоз) ҳеҷ гоҳ SentByUserId/SentByUserName надорад.
        var message = CreateMessage(Guid.NewGuid());
        message.SentByUserId = null;
        message.SentByUserName = null;

        var dto = MessageDto.FromEntity(message);

        Assert.Null(dto.SentByUserId);
        Assert.Null(dto.SentByUserName);
    }

    [Fact]
    public void FromEntity_ReadsTheStoredSnapshotNotTheNavigation()
    {
        // SentByUserName — snapshot дар лаҳзаи фиристодан, на SentByUser?.FullName.
        // Санҷиш махсусан бе SentByUser (Include нашуда) — то тасдиқ кунад FromEntity
        // ба navigation вобаста нест.
        var message = CreateMessage(Guid.NewGuid());
        message.Direction = MessageDirection.Outbound;
        message.SentByUserId = Guid.NewGuid();
        message.SentByUserName = "Operator Name";
        message.SentByUser = null;

        var dto = MessageDto.FromEntity(message);

        Assert.Equal("Operator Name", dto.SentByUserName);
    }

    [Fact]
    public void FromEntity_SentByUserDeleted_SnapshotSurvivesEvenThoughTheFkWentNull()
    {
        // SentByUserId → SET NULL ҳангоми нест кардани корбар, вале SentByUserName
        // (snapshot-и мустақил) мемонад — ин тамоми сабаби вуҷуди ин колонка аст.
        var message = CreateMessage(Guid.NewGuid());
        message.Direction = MessageDirection.Outbound;
        message.SentByUserId = null;
        message.SentByUserName = "Former Operator";

        var dto = MessageDto.FromEntity(message);

        Assert.Null(dto.SentByUserId);
        Assert.Equal("Former Operator", dto.SentByUserName);
    }

    [Fact]
    public void FromEntity_DeletedMedia_CarriesMediaDeletedAt()
    {
        var message = CreateMessage(Guid.NewGuid());
        var deletedAt = DateTimeOffset.UtcNow;
        message.MediaDeletedAt = deletedAt;

        var dto = MessageDto.FromEntity(message);

        Assert.Equal(deletedAt, dto.MediaDeletedAt);
    }

    [Fact]
    public void FromEntity_NoWaveformPeaks_WaveformPeaksIsNull()
    {
        var message = CreateMessage(Guid.NewGuid());
        message.Type = MessageType.Audio;
        message.WaveformPeaks = null;

        var dto = MessageDto.FromEntity(message);

        Assert.Null(dto.WaveformPeaks);
    }

    [Fact]
    public void FromEntity_WaveformPeaksStored_ConvertsZeroToHundredIntoZeroToOneFloats()
    {
        var message = CreateMessage(Guid.NewGuid());
        message.Type = MessageType.Audio;
        message.WaveformPeaks = [0, 25, 50, 75, 100];

        var dto = MessageDto.FromEntity(message);

        Assert.Equal([0.0, 0.25, 0.5, 0.75, 1.0], dto.WaveformPeaks);
    }

    [Fact]
    public void FromEntity_DeletedAudio_StillSurvivesWithWaveformPeaks()
    {
        // Қарори қасдӣ: peaks (ба монанди thumbnail) аз тозакунии retention халос
        // мешавад — playback имконнопазир аст, вале шакли мавҷ дар архив мемонад.
        var message = CreateMessage(Guid.NewGuid());
        message.Type = MessageType.Audio;
        message.WaveformPeaks = [10, 20, 30];
        message.MediaDeletedAt = DateTimeOffset.UtcNow;

        var dto = MessageDto.FromEntity(message);

        Assert.NotNull(dto.WaveformPeaks);
        Assert.Equal(3, dto.WaveformPeaks.Count);
    }
}
