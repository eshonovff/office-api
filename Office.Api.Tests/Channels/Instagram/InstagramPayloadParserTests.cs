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

    // URL воқеан пайванди саҳифаи веб аст (масалан https://www.instagram.com/reel/<code>/), на
    // URL-и CDN — тасдиқшуда бо curl зидди production-и воқеӣ 2026-08-24 (ниг. InstagramPayloadParser
    // барои тафсил). Ин фикстура қасдан ҳамин шаклро истифода мебарад, на шакли CDN-монанди пештара.
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
                        "payload": { "url": "https://www.instagram.com/reel/DWJ5EU-Ad5i/", "title": "funny cat" }
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
                      { "type": "ig_reel", "payload": { "url": "https://www.instagram.com/reel/AbCdEfGhIjK/" } }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

    // URL — далели воқеии production (2026-08-25, ниг. InstagramPayloadParser): барои ig_post
    // ин URL-и ВОҚЕИИ CDN аст (lookaside.fbsbx.com/ig_messaging_cdn/...), на пайванди веб — ig_reel
    // фарқ мекунад (он ҷо instagram.com/reel/<code>/, тасдиқшуда бо curl). ig_post_media_id ҳам
    // ҳамроҳи url меояд, вале ҳанӯз истифода намешавад.
    private const string PostAttachmentPayload = """
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
                    "mid": "aWdfZAG1fUE9TVA",
                    "attachments": [
                      {
                        "type": "ig_post",
                        "payload": {
                          "url": "https://lookaside.fbsbx.com/ig_messaging_cdn/?asset_id=18614582272044180&signature=abc123",
                          "title": "sunset",
                          "ig_post_media_id": "18614582272044180"
                        }
                      }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

    // Далели аввалини (санҷиданашудаи) карусел — якчанд attachment дар як паём. Шакли воқеии
    // payload-и Meta барои карусел ҳанӯз дида нашудааст, ниг. InstagramPayloadParser барои сабаб.
    private const string CarouselPostAttachmentPayload = """
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
                    "mid": "aWdfZAG1fQ0FST1VTRUw",
                    "attachments": [
                      { "type": "ig_post", "payload": { "url": "https://lookaside.fbsbx.com/ig_messaging_cdn/?asset_id=1", "title": "trip" } },
                      { "type": "ig_post", "payload": { "url": "https://lookaside.fbsbx.com/ig_messaging_cdn/?asset_id=2" } }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

    private const string LikeHeartPayload = """
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
                    "mid": "aWdfZAG1fHEART",
                    "attachments": [
                      { "type": "like_heart", "payload": {} }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

    private const string ReactionPayload = """
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
                  "reaction": { "mid": "aWdfZAG1fORIGINAL", "action": "react", "reaction": "love", "emoji": "❤" }
                }
              ]
            }
          ]
        }
        """;

    private const string UnreactPayload = """
        {
          "object": "instagram",
          "entry": [
            {
              "id": "17841400000000000",
              "messaging": [
                {
                  "sender": { "id": "1254001234567890" },
                  "recipient": { "id": "17841400000000000" },
                  "timestamp": 1569262486200,
                  "reaction": { "mid": "aWdfZAG1fORIGINAL", "action": "unreact" }
                }
              ]
            }
          ]
        }
        """;

    private const string VideoAttachmentPayload = """
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
                    "mid": "aWdfZAG1fVIDEO",
                    "attachments": [
                      { "type": "video", "payload": { "url": "https://scontent.cdninstagram.com/v/clip.mp4?expires=123" } }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

    private const string AudioAttachmentPayload = """
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
                    "mid": "aWdfZAG1fAUDIO",
                    "attachments": [
                      { "type": "audio", "payload": { "url": "https://scontent.cdninstagram.com/v/voice.aac?expires=123" } }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

    private const string FileAttachmentPayload = """
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
                    "mid": "aWdfZAG1fFILE",
                    "attachments": [
                      { "type": "file", "payload": { "url": "https://scontent.cdninstagram.com/v/doc.pdf?expires=123" } }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

    private const string UnknownAttachmentTypePayload = """
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
                    "mid": "aWdfZAG1fWEIRD",
                    "attachments": [
                      { "type": "some_future_type", "payload": { "url": "https://scontent.cdninstagram.com/v/x?expires=123" } }
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
    public void ParseMessages_StoryReply_MapsToStoryReplyWithTextAndExternalContentUrl()
    {
        var messages = InstagramPayloadParser.ParseMessages(Parse(StoryReplyPayload));

        var message = Assert.Single(messages);
        Assert.Equal(MessageType.StoryReply, message.Type);
        Assert.Equal("cool story!", message.Body);
        // Same treatment as ig_reel/ig_post below: never trust an "external Instagram content"
        // url as downloadable CDN media without live confirmation — MediaExternalId stays null,
        // the url only travels as ExternalContentUrl for the frontend's own link-out.
        Assert.Null(message.MediaExternalId);
        Assert.Equal("https://scontent.cdninstagram.com/v/story.jpg?expires=456", message.ExternalContentUrl);
        Assert.Equal("Story", message.ExternalContentKind);
    }

    [Fact]
    public void ParseMessages_StoryMention_MapsToStoryReplyWithNoBody()
    {
        var messages = InstagramPayloadParser.ParseMessages(Parse(StoryMentionPayload));

        var message = Assert.Single(messages);
        Assert.Equal(MessageType.StoryReply, message.Type);
        Assert.Null(message.Body);
        Assert.Null(message.MediaExternalId);
        Assert.Equal("https://scontent.cdninstagram.com/v/mention.jpg?expires=789", message.ExternalContentUrl);
        Assert.Equal("Story", message.ExternalContentKind);
    }

    [Fact]
    public void ParseMessages_Reel_MapsToVideoWithReelMarkerAndTitle()
    {
        var messages = InstagramPayloadParser.ParseMessages(Parse(ReelAttachmentPayload));

        var message = Assert.Single(messages);
        Assert.Equal(MessageType.Video, message.Type);
        // payload.url is a web permalink, not a CDN asset (confirmed live, see the parser's
        // comment) — it's a dedicated field now, not embedded in Body (an earlier version put it
        // on a second Body line, which the "open in Instagram" button ended up mishandling).
        Assert.Equal("[Reel] funny cat", message.Body);
        Assert.Null(message.MediaExternalId);
        Assert.Equal("https://www.instagram.com/reel/DWJ5EU-Ad5i/", message.ExternalContentUrl);
        Assert.Equal("Reel", message.ExternalContentKind);
    }

    [Fact]
    public void ParseMessages_ReelWithoutTitle_MapsToVideoWithBareMarker()
    {
        var messages = InstagramPayloadParser.ParseMessages(Parse(ReelAttachmentNoTitlePayload));

        var message = Assert.Single(messages);
        Assert.Equal(MessageType.Video, message.Type);
        Assert.Equal("[Reel]", message.Body);
        Assert.Null(message.MediaExternalId);
        Assert.Equal("https://www.instagram.com/reel/AbCdEfGhIjK/", message.ExternalContentUrl);
    }

    [Fact]
    public void ParseMessages_Post_MapsToVideoWithPostMarkerAndIsDownloadable()
    {
        // ig_post used to fall through to the unsupported-type branch entirely, then (in an
        // earlier fix) was wrongly treated as non-downloadable like ig_reel — live production
        // evidence (2026-08-25) proved that wrong: unlike ig_reel's web permalink, ig_post's url
        // really is a lookaside.fbsbx.com CDN asset (confirmed: MediaDownloadJob fetched one
        // successfully). MediaExternalId is populated so it downloads through the normal path;
        // ExternalContentUrl/Kind are ALSO populated so the frontend can still offer an
        // "open in Instagram" link alongside the real player.
        var messages = InstagramPayloadParser.ParseMessages(Parse(PostAttachmentPayload));

        var message = Assert.Single(messages);
        Assert.Equal(MessageType.Video, message.Type);
        Assert.Equal("[Post] sunset", message.Body);
        Assert.Equal("https://lookaside.fbsbx.com/ig_messaging_cdn/?asset_id=18614582272044180&signature=abc123", message.MediaExternalId);
        Assert.Equal(message.MediaExternalId, message.ExternalContentUrl);
        Assert.Equal("Post", message.ExternalContentKind);
    }

    [Fact]
    public void ParseMessages_CarouselPost_UsesFirstItemAndFlagsTheRest()
    {
        // Only attachments[0] is used, same as every other type here — but for a carousel that's
        // a real, visible gap (not silent): the "(+N боз)" marker is the evidence trail for the
        // day a real carousel payload's shape gets confirmed and this can be done properly.
        var messages = InstagramPayloadParser.ParseMessages(Parse(CarouselPostAttachmentPayload));

        var message = Assert.Single(messages);
        Assert.Equal(MessageType.Video, message.Type);
        Assert.Equal("[Post] trip (+1 боз)", message.Body);
        Assert.Equal("https://lookaside.fbsbx.com/ig_messaging_cdn/?asset_id=1", message.MediaExternalId);
    }

    [Fact]
    public void ExtractExternalContentPayloadsForDiagnostics_ReturnsRawPayloadForReelPostAndStory()
    {
        var reelPayloads = InstagramPayloadParser.ExtractExternalContentPayloadsForDiagnostics(Parse(ReelAttachmentPayload));
        var storyReplyPayloads = InstagramPayloadParser.ExtractExternalContentPayloadsForDiagnostics(Parse(StoryReplyPayload));
        var storyMentionPayloads = InstagramPayloadParser.ExtractExternalContentPayloadsForDiagnostics(Parse(StoryMentionPayload));
        var imagePayloads = InstagramPayloadParser.ExtractExternalContentPayloadsForDiagnostics(Parse(ImageAttachmentPayload));

        Assert.Single(reelPayloads);
        Assert.Contains("DWJ5EU-Ad5i", reelPayloads[0]);
        Assert.Single(storyReplyPayloads);
        Assert.Contains("story.jpg", storyReplyPayloads[0]);
        Assert.Single(storyMentionPayloads);
        // Not ig_reel/ig_post/story_mention/reply_to.story — nothing to diagnose here.
        Assert.Empty(imagePayloads);
    }

    [Fact]
    public void ParseMessages_LikeHeart_MapsToTextWithStickerMarker()
    {
        var messages = InstagramPayloadParser.ParseMessages(Parse(LikeHeartPayload));

        var message = Assert.Single(messages);
        Assert.Equal(MessageType.Text, message.Type);
        Assert.NotNull(message.Body);
        Assert.Null(message.MediaExternalId);
    }

    [Fact]
    public void ParseMessages_Reaction_RecordsAsTextWithEmojiAndSyntheticId()
    {
        var messages = InstagramPayloadParser.ParseMessages(Parse(ReactionPayload));

        var message = Assert.Single(messages);
        Assert.Equal(MessageType.Text, message.Type);
        Assert.Contains("❤", message.Body);
        Assert.Equal("reaction:1254001234567890:1569262486134", message.MessageExternalId);
    }

    [Fact]
    public void ParseMessages_Unreact_RecordsDistinctlyFromReact()
    {
        var messages = InstagramPayloadParser.ParseMessages(Parse(UnreactPayload));

        var message = Assert.Single(messages);
        Assert.Equal(MessageType.Text, message.Type);
        Assert.DoesNotContain("❤", message.Body);
    }

    [Fact]
    public void ParseMessages_VideoAttachment_MapsToVideo()
    {
        var messages = InstagramPayloadParser.ParseMessages(Parse(VideoAttachmentPayload));

        var message = Assert.Single(messages);
        Assert.Equal(MessageType.Video, message.Type);
        Assert.Equal("https://scontent.cdninstagram.com/v/clip.mp4?expires=123", message.MediaExternalId);
    }

    [Fact]
    public void ParseMessages_AudioAttachment_MapsToAudio()
    {
        var messages = InstagramPayloadParser.ParseMessages(Parse(AudioAttachmentPayload));

        var message = Assert.Single(messages);
        Assert.Equal(MessageType.Audio, message.Type);
        Assert.Equal("https://scontent.cdninstagram.com/v/voice.aac?expires=123", message.MediaExternalId);
    }

    [Fact]
    public void ParseMessages_FileAttachment_MapsToFile()
    {
        var messages = InstagramPayloadParser.ParseMessages(Parse(FileAttachmentPayload));

        var message = Assert.Single(messages);
        Assert.Equal(MessageType.File, message.Type);
        Assert.Equal("https://scontent.cdninstagram.com/v/doc.pdf?expires=123", message.MediaExternalId);
    }

    [Fact]
    public void ParseMessages_UnknownAttachmentType_RecordsMarkerInsteadOfDroppingSilently()
    {
        var messages = InstagramPayloadParser.ParseMessages(Parse(UnknownAttachmentTypePayload));

        var message = Assert.Single(messages);
        Assert.Equal(MessageType.Text, message.Type);
        Assert.StartsWith(InstagramPayloadParser.UnsupportedTypeBodyPrefix, message.Body);
        Assert.Contains("some_future_type", message.Body);
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
