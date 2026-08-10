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
    public void FromEntity_SentByUserNotLoaded_SentByUserNameIsNullNotAnException()
    {
        // Inbound-и вебҳук ва боркунии медиа SentByUser-ро Include намекунанд (доим null
        // барои паёми воридотӣ) — FromEntity набояд аз ин афтад.
        var message = CreateMessage(Guid.NewGuid());
        message.SentByUserId = null;

        var dto = MessageDto.FromEntity(message);

        Assert.Null(dto.SentByUserId);
        Assert.Null(dto.SentByUserName);
    }

    [Fact]
    public void FromEntity_SentByUserLoaded_CarriesTheFullName()
    {
        var message = CreateMessage(Guid.NewGuid());
        message.Direction = MessageDirection.Outbound;
        message.SentByUserId = Guid.NewGuid();
        message.SentByUser = new User
        {
            Id = message.SentByUserId.Value,
            FullName = "Operator Name",
            Username = "operator",
            PasswordHash = "hash",
        };

        var dto = MessageDto.FromEntity(message);

        Assert.Equal("Operator Name", dto.SentByUserName);
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
}
