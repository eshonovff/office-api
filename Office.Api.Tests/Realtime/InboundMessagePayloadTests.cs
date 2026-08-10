using Office.Api.Data.Entities;
using Office.Api.Realtime;

namespace Office.Api.Tests.Realtime;

public class InboundMessagePayloadTests
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

        var payload = InboundMessagePayload.FromEntity(message);

        Assert.Null(payload.MediaUrl);
        Assert.Null(payload.ThumbnailUrl);
    }

    [Fact]
    public void FromEntity_MediaDownloaded_MediaUrlIsTheEndpointPathNotTheRawStoragePath()
    {
        var id = Guid.NewGuid();
        var message = CreateMessage(id);
        message.MediaUrl = "whatsapp-media/some-channel/019feb.jpg";

        var payload = InboundMessagePayload.FromEntity(message);

        Assert.Equal($"/api/messages/{id}/media", payload.MediaUrl);
        Assert.DoesNotContain("whatsapp-media", payload.MediaUrl);
    }

    [Fact]
    public void FromEntity_ThumbnailGenerated_ThumbnailUrlIsTheEndpointPath()
    {
        var id = Guid.NewGuid();
        var message = CreateMessage(id);
        message.ThumbnailUrl = "whatsapp-media/some-channel/019feb_thumb.jpg";

        var payload = InboundMessagePayload.FromEntity(message);

        Assert.Equal($"/api/messages/{id}/thumbnail", payload.ThumbnailUrl);
    }

    [Fact]
    public void FromEntity_DownloadFailed_CarriesTheErrorAndNoMediaUrl()
    {
        var message = CreateMessage(Guid.NewGuid());
        message.MediaDownloadError = "timed out after 5 retries";

        var payload = InboundMessagePayload.FromEntity(message);

        Assert.Equal("timed out after 5 retries", payload.MediaDownloadError);
        Assert.Null(payload.MediaUrl);
    }

    [Fact]
    public void FromEntity_MapsEnumsAndScalarFieldsAsStrings()
    {
        var message = CreateMessage(Guid.NewGuid());
        message.Type = MessageType.Audio;
        message.Direction = MessageDirection.Inbound;
        message.DeliveryStatus = MessageDeliveryStatus.Read;
        message.VoiceDurationSeconds = 7;

        var payload = InboundMessagePayload.FromEntity(message);

        Assert.Equal("Audio", payload.Type);
        Assert.Equal("Inbound", payload.Direction);
        Assert.Equal("Read", payload.DeliveryStatus);
        Assert.Equal(7, payload.VoiceDurationSeconds);
    }
}
