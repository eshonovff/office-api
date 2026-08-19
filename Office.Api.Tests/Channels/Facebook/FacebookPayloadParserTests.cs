using System.Text.Json;
using Office.Api.Channels.Facebook;
using Office.Api.Data.Entities;

namespace Office.Api.Tests.Channels.Facebook;

public class FacebookPayloadParserTests
{
    // Шаклҳо аз ҳуҷҷати расмии Messenger Platform (Webhook Reference / Send API).
    private const string TextMessagePayload = """
        {
          "object": "page",
          "entry": [
            {
              "id": "1234567890",
              "time": 1458692752478,
              "messaging": [
                {
                  "sender": { "id": "1000000000000001" },
                  "recipient": { "id": "1234567890" },
                  "timestamp": 1458692752478,
                  "message": {
                    "mid": "mid.1457764197618:41d102a3e1ae206a38",
                    "text": "hello, world!"
                  }
                }
              ]
            }
          ]
        }
        """;

    private const string EchoPayload = """
        {
          "object": "page",
          "entry": [
            {
              "id": "1234567890",
              "messaging": [
                {
                  "sender": { "id": "1234567890" },
                  "recipient": { "id": "1000000000000001" },
                  "timestamp": 1458692752999,
                  "message": {
                    "is_echo": true,
                    "mid": "mid.ECHO",
                    "text": "reply from the page itself"
                  }
                }
              ]
            }
          ]
        }
        """;

    private const string ImageAttachmentPayload = """
        {
          "object": "page",
          "entry": [
            {
              "id": "1234567890",
              "messaging": [
                {
                  "sender": { "id": "1000000000000001" },
                  "recipient": { "id": "1234567890" },
                  "timestamp": 1458692752478,
                  "message": {
                    "mid": "mid.ATTACH",
                    "attachments": [
                      { "type": "image", "payload": { "url": "https://scontent.xx.fbcdn.net/v/image.jpg?expires=123" } }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

    private const string PostbackPayload = """
        {
          "object": "page",
          "entry": [
            {
              "id": "1234567890",
              "messaging": [
                {
                  "sender": { "id": "1000000000000001" },
                  "recipient": { "id": "1234567890" },
                  "timestamp": 1458692752600,
                  "postback": { "title": "Get Started", "payload": "GET_STARTED_PAYLOAD" }
                }
              ]
            }
          ]
        }
        """;

    private const string ReelAttachmentPayload = """
        {
          "object": "page",
          "entry": [
            {
              "id": "1234567890",
              "messaging": [
                {
                  "sender": { "id": "1000000000000001" },
                  "recipient": { "id": "1234567890" },
                  "timestamp": 1458692752478,
                  "message": {
                    "mid": "mid.REEL",
                    "attachments": [
                      { "type": "reel", "payload": { "url": "https://scontent.xx.fbcdn.net/v/reel.mp4?expires=123", "title": "funny cat" } }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

    private const string DeliveryPayload = """
        {
          "object": "page",
          "entry": [
            {
              "id": "1234567890",
              "messaging": [
                {
                  "sender": { "id": "1000000000000001" },
                  "recipient": { "id": "1234567890" },
                  "timestamp": 1458668856253,
                  "delivery": { "mids": ["mid.1458668856218:ba04a4f2"], "watermark": 1458668856253 }
                }
              ]
            }
          ]
        }
        """;

    private const string ReadPayload = """
        {
          "object": "page",
          "entry": [
            {
              "id": "1234567890",
              "messaging": [
                {
                  "sender": { "id": "1000000000000001" },
                  "recipient": { "id": "1234567890" },
                  "timestamp": 1458668856253,
                  "read": { "watermark": 1458668856253 }
                }
              ]
            }
          ]
        }
        """;

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void ExtractChannelExternalId_ReturnsPageId()
    {
        Assert.Equal("1234567890", FacebookPayloadParser.ExtractChannelExternalId(Parse(TextMessagePayload)));
    }

    [Fact]
    public void ExtractSentMessageId_ReturnsMessageId()
    {
        const string response = """{"recipient_id":"1254520233","message_id":"mid.1457764197618:41d102a3e1ae206a38"}""";
        Assert.Equal("mid.1457764197618:41d102a3e1ae206a38", FacebookPayloadParser.ExtractSentMessageId(response));
    }

    [Fact]
    public void ParseMessages_TextMessage_ReturnsInboundText()
    {
        var messages = FacebookPayloadParser.ParseMessages(Parse(TextMessagePayload));

        var message = Assert.Single(messages);
        Assert.Equal("1000000000000001", message.ConversationExternalId);
        Assert.Equal("mid.1457764197618:41d102a3e1ae206a38", message.MessageExternalId);
        Assert.Equal(MessageDirection.Inbound, message.Direction);
        Assert.Equal(MessageType.Text, message.Type);
        Assert.Equal("hello, world!", message.Body);
        Assert.Null(message.MediaExternalId);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1458692752478), message.SentAt);
    }

    [Fact]
    public void ParseMessages_Echo_IsSkipped()
    {
        var messages = FacebookPayloadParser.ParseMessages(Parse(EchoPayload));

        Assert.Empty(messages);
    }

    [Fact]
    public void ParseMessages_ImageAttachment_MapsUrlToMediaExternalIdNotMediaUrl()
    {
        var messages = FacebookPayloadParser.ParseMessages(Parse(ImageAttachmentPayload));

        var message = Assert.Single(messages);
        Assert.Equal(MessageType.Image, message.Type);
        Assert.Null(message.MediaUrl);
        Assert.Equal("https://scontent.xx.fbcdn.net/v/image.jpg?expires=123", message.MediaExternalId);
    }

    [Fact]
    public void ParseMessages_Postback_ProducesSyntheticIdempotentId()
    {
        var messages = FacebookPayloadParser.ParseMessages(Parse(PostbackPayload));

        var message = Assert.Single(messages);
        Assert.Equal("Get Started", message.Body);
        Assert.Equal(MessageType.Text, message.Type);
        Assert.Equal("postback:1000000000000001:1458692752600", message.MessageExternalId);

        // Такрори як webhook (redelivery) — ҳамон id, пас MessageIdempotencyPlanner дубора сабт намекунад.
        var repeated = FacebookPayloadParser.ParseMessages(Parse(PostbackPayload));
        Assert.Equal(message.MessageExternalId, Assert.Single(repeated).MessageExternalId);
    }

    [Fact]
    public void ParseMessages_Reel_MapsToVideoWithReelMarkerAndTitle()
    {
        var messages = FacebookPayloadParser.ParseMessages(Parse(ReelAttachmentPayload));

        var message = Assert.Single(messages);
        Assert.Equal(MessageType.Video, message.Type);
        Assert.Equal("[Reel] funny cat", message.Body);
        Assert.Equal("https://scontent.xx.fbcdn.net/v/reel.mp4?expires=123", message.MediaExternalId);
    }

    [Fact]
    public void ParseStatusUpdates_Delivery_ReturnsDeliveredForEachMid()
    {
        var updates = FacebookPayloadParser.ParseStatusUpdates(Parse(DeliveryPayload));

        var update = Assert.Single(updates);
        Assert.Equal("mid.1458668856218:ba04a4f2", update.MessageExternalId);
        Assert.Equal(MessageDeliveryStatus.Delivered, update.Status);
    }

    [Fact]
    public void ParseStatusUpdates_Read_IsIgnored()
    {
        // "read" як watermark аст (то ин вақт), на mid-и мушаххас — ParsedStatusUpdate ин шаклро
        // ифода карда наметавонад, пас қасдан партофта мешавад (ниг. FacebookPayloadParser).
        var updates = FacebookPayloadParser.ParseStatusUpdates(Parse(ReadPayload));

        Assert.Empty(updates);
    }
}
