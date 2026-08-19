using System.Text.Json;
using Office.Api.Channels.Instagram;
using Office.Api.Data.Entities;

namespace Office.Api.Tests.Channels.Instagram;

public class InstagramPayloadParserTests
{
    // Шаклҳо аз ҳуҷҷати расмии Instagram Messaging API (Webhooks / Send API reference).
    private const string TextMessagePayload = """
        {
          "object": "instagram",
          "entry": [
            {
              "id": "17841400000000000",
              "time": 1569262486134,
              "messaging": [
                {
                  "sender": { "id": "1254001234567890" },
                  "recipient": { "id": "17841400000000000" },
                  "timestamp": 1569262486134,
                  "message": { "mid": "aWdfZAG1faXRlbToxOd", "text": "hello" }
                }
              ]
            }
          ]
        }
        """;

    private const string EchoPayload = """
        {
          "object": "instagram",
          "entry": [
            {
              "id": "17841400000000000",
              "messaging": [
                {
                  "sender": { "id": "17841400000000000" },
                  "recipient": { "id": "1254001234567890" },
                  "timestamp": 1569262486999,
                  "message": { "is_echo": true, "mid": "aWdfZAG1ECHO", "text": "reply from the account itself" }
                }
              ]
            }
          ]
        }
        """;

    private const string ImageAttachmentPayload = """
        {
          "object": "instagram",
          "entry": [
            {
              "id": "17841400000000000",
              "messaging": [
                {
                  "sender": { "id": "1254001234567890" },
                  "recipient": { "id": "17841400000000000" },
                  "timestamp": 1569262486134,
                  "message": {
                    "mid": "aWdfZAG1fATTACH",
                    "attachments": [
                      { "type": "image", "payload": { "url": "https://scontent.cdninstagram.com/v/photo.jpg?expires=123" } }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

    private const string StoryReplyPayload = """
        {
          "object": "instagram",
          "entry": [
            {
              "id": "17841400000000000",
              "messaging": [
                {
                  "sender": { "id": "1254001234567890" },
                  "recipient": { "id": "17841400000000000" },
                  "timestamp": 1569262486134,
                  "message": {
                    "mid": "aWdfZAG1fREPLY",
                    "text": "cool story!",
                    "reply_to": { "story": { "url": "https://scontent.cdninstagram.com/v/story.jpg?expires=456", "id": "17900000000000000" } }
                  }
                }
              ]
            }
          ]
        }
        """;

    private const string StoryMentionPayload = """
        {
          "object": "instagram",
          "entry": [
            {
              "id": "17841400000000000",
              "messaging": [
                {
                  "sender": { "id": "1254001234567890" },
                  "recipient": { "id": "17841400000000000" },
                  "timestamp": 1569262486134,
                  "message": {
                    "mid": "aWdfZAG1fMENTION",
                    "attachments": [
                      { "type": "story_mention", "payload": { "url": "https://scontent.cdninstagram.com/v/mention.jpg?expires=789" } }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

    private const string ReelAttachmentPayload = """
        {
          "object": "instagram",
          "entry": [
            {
              "id": "17841400000000000",
              "messaging": [
                {
                  "sender": { "id": "1254001234567890" },
                  "recipient": { "id": "17841400000000000" },
                  "timestamp": 1569262486134,
                  "message": {
                    "mid": "aWdfZAG1fREEL",
                    "attachments": [
                      {
                        "type": "ig_reel",
                        "payload": { "url": "https://scontent.cdninstagram.com/v/reel.mp4?expires=123", "title": "funny cat" }
                      }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

    private const string ReelAttachmentNoTitlePayload = """
        {
          "object": "instagram",
          "entry": [
            {
              "id": "17841400000000000",
              "messaging": [
                {
                  "sender": { "id": "1254001234567890" },
                  "recipient": { "id": "17841400000000000" },
                  "timestamp": 1569262486134,
                  "message": {
                    "mid": "aWdfZAG1fREEL2",
                    "attachments": [
                      { "type": "ig_reel", "payload": { "url": "https://scontent.cdninstagram.com/v/reel2.mp4?expires=123" } }
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
          "object": "instagram",
          "entry": [
            {
              "id": "17841400000000000",
              "messaging": [
                {
                  "sender": { "id": "1254001234567890" },
                  "recipient": { "id": "17841400000000000" },
                  "timestamp": 1569262490000,
                  "delivery": { "mids": ["aWdfZAG1fATTACH"], "watermark": 1569262490000 }
                }
              ]
            }
          ]
        }
        """;

    private const string ReadPayload = """
        {
          "object": "instagram",
          "entry": [
            {
              "id": "17841400000000000",
              "messaging": [
                {
                  "sender": { "id": "1254001234567890" },
                  "recipient": { "id": "17841400000000000" },
                  "timestamp": 1569262490000,
                  "read": { "watermark": 1569262490000 }
                }
              ]
            }
          ]
        }
        """;

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void ExtractChannelExternalId_ReturnsInstagramAccountId()
    {
        Assert.Equal("17841400000000000", InstagramPayloadParser.ExtractChannelExternalId(Parse(TextMessagePayload)));
    }

    [Fact]
    public void ExtractSentMessageId_ReturnsMessageId()
    {
        const string response = """{"recipient_id":"1254001234567890","message_id":"aWdfZAG1fSENT"}""";
        Assert.Equal("aWdfZAG1fSENT", InstagramPayloadParser.ExtractSentMessageId(response));
    }

    [Fact]
    public void ParseMessages_TextMessage_ReturnsInboundText()
    {
        var messages = InstagramPayloadParser.ParseMessages(Parse(TextMessagePayload));

        var message = Assert.Single(messages);
        Assert.Equal("1254001234567890", message.ConversationExternalId);
        Assert.Equal("aWdfZAG1faXRlbToxOd", message.MessageExternalId);
        Assert.Equal(MessageDirection.Inbound, message.Direction);
        Assert.Equal(MessageType.Text, message.Type);
        Assert.Equal("hello", message.Body);
    }

    [Fact]
    public void ParseMessages_Echo_IsSkipped()
    {
        Assert.Empty(InstagramPayloadParser.ParseMessages(Parse(EchoPayload)));
    }

    [Fact]
    public void ParseMessages_ImageAttachment_MapsUrlToMediaExternalIdNotMediaUrl()
    {
        var messages = InstagramPayloadParser.ParseMessages(Parse(ImageAttachmentPayload));

        var message = Assert.Single(messages);
        Assert.Equal(MessageType.Image, message.Type);
        Assert.Null(message.MediaUrl);
        Assert.Equal("https://scontent.cdninstagram.com/v/photo.jpg?expires=123", message.MediaExternalId);
    }

    [Fact]
    public void ParseMessages_StoryReply_MapsToStoryReplyWithTextAndStoryImage()
    {
        var messages = InstagramPayloadParser.ParseMessages(Parse(StoryReplyPayload));

        var message = Assert.Single(messages);
        Assert.Equal(MessageType.StoryReply, message.Type);
        Assert.Equal("cool story!", message.Body);
        Assert.Equal("https://scontent.cdninstagram.com/v/story.jpg?expires=456", message.MediaExternalId);
    }

    [Fact]
    public void ParseMessages_StoryMention_MapsToStoryReplyWithNoBody()
    {
        var messages = InstagramPayloadParser.ParseMessages(Parse(StoryMentionPayload));

        var message = Assert.Single(messages);
        Assert.Equal(MessageType.StoryReply, message.Type);
        Assert.Null(message.Body);
        Assert.Equal("https://scontent.cdninstagram.com/v/mention.jpg?expires=789", message.MediaExternalId);
    }

    [Fact]
    public void ParseMessages_Reel_MapsToVideoWithReelMarkerAndTitle()
    {
        var messages = InstagramPayloadParser.ParseMessages(Parse(ReelAttachmentPayload));

        var message = Assert.Single(messages);
        Assert.Equal(MessageType.Video, message.Type);
        Assert.Equal("[Reel] funny cat", message.Body);
        Assert.Equal("https://scontent.cdninstagram.com/v/reel.mp4?expires=123", message.MediaExternalId);
    }

    [Fact]
    public void ParseMessages_ReelWithoutTitle_MapsToVideoWithBareMarker()
    {
        var messages = InstagramPayloadParser.ParseMessages(Parse(ReelAttachmentNoTitlePayload));

        var message = Assert.Single(messages);
        Assert.Equal(MessageType.Video, message.Type);
        Assert.Equal("[Reel]", message.Body);
    }

    [Fact]
    public void ParseStatusUpdates_Delivery_ReturnsDeliveredForEachMid()
    {
        var updates = InstagramPayloadParser.ParseStatusUpdates(Parse(DeliveryPayload));

        var update = Assert.Single(updates);
        Assert.Equal("aWdfZAG1fATTACH", update.MessageExternalId);
        Assert.Equal(MessageDeliveryStatus.Delivered, update.Status);
    }

    [Fact]
    public void ParseStatusUpdates_Read_IsIgnored()
    {
        Assert.Empty(InstagramPayloadParser.ParseStatusUpdates(Parse(ReadPayload)));
    }
}
