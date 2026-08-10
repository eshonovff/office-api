using System.Text.Json;
using Office.Api.Channels.WhatsApp;
using Office.Api.Data.Entities;

namespace Office.Api.Tests.Channels.WhatsApp;

public class WhatsAppPayloadParserTests
{
    [Fact]
    public void ExtractChannelExternalId_ValidPayload_ReturnsPhoneNumberId()
    {
        using var doc = JsonDocument.Parse("""
            {"entry":[{"changes":[{"value":{"metadata":{"phone_number_id":"7794189252778687"}}}]}]}
            """);

        Assert.Equal("7794189252778687", WhatsAppPayloadParser.ExtractChannelExternalId(doc.RootElement));
    }

    [Fact]
    public void ExtractChannelExternalId_MissingEntry_ReturnsNull()
    {
        using var doc = JsonDocument.Parse("{}");
        Assert.Null(WhatsAppPayloadParser.ExtractChannelExternalId(doc.RootElement));
    }

    [Fact]
    public void ParseMessages_TextMessage_ReturnsSingleMessage()
    {
        using var doc = JsonDocument.Parse("""
            {"entry":[{"changes":[{"value":{
              "metadata":{"phone_number_id":"123"},
              "contacts":[{"profile":{"name":"Jessica"},"wa_id":"17863559966"}],
              "messages":[{"from":"17863559966","id":"wamid.ABC","timestamp":"1758254144","type":"text","text":{"body":"Hi!"}}]
            }}]}]}
            """);

        var result = WhatsAppPayloadParser.ParseMessages(doc.RootElement);

        var message = Assert.Single(result);
        Assert.Equal("17863559966", message.ConversationExternalId);
        Assert.Equal("Jessica", message.ContactName);
        Assert.Equal("wamid.ABC", message.MessageExternalId);
        Assert.Equal(MessageDirection.Inbound, message.Direction);
        Assert.Equal(MessageType.Text, message.Type);
        Assert.Equal("Hi!", message.Body);
        Assert.Null(message.MediaExternalId);
    }

    [Fact]
    public void ParseMessages_ImageMessage_CapturesMediaExternalId()
    {
        using var doc = JsonDocument.Parse("""
            {"entry":[{"changes":[{"value":{
              "messages":[{"from":"17863559966","id":"wamid.IMG","timestamp":"1758254144","type":"image",
                "image":{"id":"media123","mime_type":"image/jpeg","caption":"Look"}}]
            }}]}]}
            """);

        var result = WhatsAppPayloadParser.ParseMessages(doc.RootElement);

        var message = Assert.Single(result);
        Assert.Equal(MessageType.Image, message.Type);
        Assert.Equal("Look", message.Body);
        Assert.Equal("media123", message.MediaExternalId);
    }

    [Fact]
    public void ParseMessages_ImageMessage_CapturesMimeType()
    {
        using var doc = JsonDocument.Parse("""
            {"entry":[{"changes":[{"value":{
              "messages":[{"from":"17863559966","id":"wamid.IMG","timestamp":"1758254144","type":"image",
                "image":{"id":"media123","mime_type":"image/jpeg","caption":"Look"}}]
            }}]}]}
            """);

        var result = WhatsAppPayloadParser.ParseMessages(doc.RootElement);

        var message = Assert.Single(result);
        Assert.Equal("image/jpeg", message.MimeType);
        Assert.Null(message.OriginalFileName);
    }

    [Fact]
    public void ParseMessages_DocumentMessage_CapturesMimeTypeAndFileName()
    {
        using var doc = JsonDocument.Parse("""
            {"entry":[{"changes":[{"value":{
              "messages":[{"from":"1","id":"wamid.DOC","timestamp":"1758254144","type":"document",
                "document":{"id":"media456","mime_type":"application/pdf","filename":"contract.pdf"}}]
            }}]}]}
            """);

        var result = WhatsAppPayloadParser.ParseMessages(doc.RootElement);

        var message = Assert.Single(result);
        Assert.Equal(MessageType.File, message.Type);
        Assert.Equal("media456", message.MediaExternalId);
        Assert.Equal("application/pdf", message.MimeType);
        Assert.Equal("contract.pdf", message.OriginalFileName);
    }

    [Fact]
    public void ParseMessages_AudioMessage_CapturesMimeType()
    {
        using var doc = JsonDocument.Parse("""
            {"entry":[{"changes":[{"value":{
              "messages":[{"from":"1","id":"wamid.AUD","timestamp":"1758254144","type":"audio",
                "audio":{"id":"media789","mime_type":"audio/ogg; codecs=opus","voice":true}}]
            }}]}]}
            """);

        var result = WhatsAppPayloadParser.ParseMessages(doc.RootElement);

        var message = Assert.Single(result);
        Assert.Equal(MessageType.Audio, message.Type);
        Assert.Equal("audio/ogg; codecs=opus", message.MimeType);
    }

    [Fact]
    public void ParseMessages_LocationMessage_FormatsBody()
    {
        using var doc = JsonDocument.Parse("""
            {"entry":[{"changes":[{"value":{
              "messages":[{"from":"1","id":"wamid.LOC","timestamp":"1758254144","type":"location",
                "location":{"latitude":38.5598,"longitude":68.7870,"name":"Office"}}]
            }}]}]}
            """);

        var result = WhatsAppPayloadParser.ParseMessages(doc.RootElement);

        var message = Assert.Single(result);
        Assert.Equal(MessageType.Location, message.Type);
        Assert.Contains("Office", message.Body);
    }

    [Fact]
    public void ParseMessages_ContactMessage_FormatsNames()
    {
        using var doc = JsonDocument.Parse("""
            {"entry":[{"changes":[{"value":{
              "messages":[{"from":"1","id":"wamid.CNT","timestamp":"1758254144","type":"contacts",
                "contacts":[{"name":{"formatted_name":"John Doe"}},{"name":{"formatted_name":"Jane Doe"}}]}]
            }}]}]}
            """);

        var result = WhatsAppPayloadParser.ParseMessages(doc.RootElement);

        var message = Assert.Single(result);
        Assert.Equal(MessageType.Contact, message.Type);
        Assert.Equal("John Doe, Jane Doe", message.Body);
    }

    [Fact]
    public void ParseMessages_NoMessagesField_ReturnsEmpty()
    {
        using var doc = JsonDocument.Parse("""{"entry":[{"changes":[{"value":{"statuses":[]}}]}]}""");
        Assert.Empty(WhatsAppPayloadParser.ParseMessages(doc.RootElement));
    }

    [Fact]
    public void ParseStatusUpdates_DeliveredStatus_ReturnsUpdate()
    {
        using var doc = JsonDocument.Parse("""
            {"entry":[{"changes":[{"value":{
              "statuses":[{"id":"wamid.ABC","status":"delivered","timestamp":"1758254200"}]
            }}]}]}
            """);

        var result = WhatsAppPayloadParser.ParseStatusUpdates(doc.RootElement);

        var update = Assert.Single(result);
        Assert.Equal("wamid.ABC", update.MessageExternalId);
        Assert.Equal(MessageDeliveryStatus.Delivered, update.Status);
    }

    [Fact]
    public void ParseStatusUpdates_UnknownStatus_IsSkipped()
    {
        using var doc = JsonDocument.Parse("""
            {"entry":[{"changes":[{"value":{"statuses":[{"id":"x","status":"weird","timestamp":"1758254200"}]}}]}]}
            """);

        Assert.Empty(WhatsAppPayloadParser.ParseStatusUpdates(doc.RootElement));
    }

    [Fact]
    public void ParseStatusUpdates_NoStatusesField_ReturnsEmpty()
    {
        using var doc = JsonDocument.Parse("""{"entry":[{"changes":[{"value":{"messages":[]}}]}]}""");
        Assert.Empty(WhatsAppPayloadParser.ParseStatusUpdates(doc.RootElement));
    }
}
