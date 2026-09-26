using System.Text.Json;
using Office.Api.Channels;
using Office.Api.Channels.WhatsApp;
using Office.Api.Data.Entities;

namespace Office.Api.Tests.Channels.ContactProfiles;

/// <summary>Only the profile API is real here: what Meta answers, and how often it was asked.</summary>
internal sealed class FakeProfileProvider(Func<string, ContactProfile>? answer = null) : IChannelProvider
{
    public Func<string, ContactProfile> Answer { get; set; } = answer ?? (_ => ContactProfile.Empty);
    public List<string> Asked { get; } = [];

    public Task<ContactProfile> GetContactProfileAsync(Channel channel, string contactExternalId, CancellationToken ct)
    {
        Asked.Add(contactExternalId);
        return Task.FromResult(Answer(contactExternalId));
    }

    public bool VerifyWebhookToken(string verifyToken) => throw new NotSupportedException();
    public string? ExtractChannelExternalId(JsonElement payload) => throw new NotSupportedException();
    public Task<IReadOnlyList<ParsedWebhookMessage>> ParseWebhookAsync(Channel channel, JsonElement payload, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<ParsedStatusUpdate>> ParseStatusUpdatesAsync(Channel channel, JsonElement payload, CancellationToken ct) => throw new NotSupportedException();
    public Task<string?> SendMessageAsync(Channel channel, string conversationExternalId, string body, string? messageTag, CancellationToken ct) => throw new NotSupportedException();
    public Task<string?> SendTemplateAsync(Channel channel, string conversationExternalId, string templateName, string languageCode, IReadOnlyList<string> parameters, CancellationToken ct) => throw new NotSupportedException();
    public Task MarkAsReadAsync(Channel channel, string messageExternalId, CancellationToken ct) => throw new NotSupportedException();
    public Task<DownloadedMedia> DownloadMediaAsync(Channel channel, string mediaExternalId, CancellationToken ct) => throw new NotSupportedException();
    public Task<string> UploadMediaAsync(Channel channel, Stream content, string mimeType, string fileName, CancellationToken ct) => throw new NotSupportedException();
    public Task<string?> SendMediaMessageAsync(Channel channel, string conversationExternalId, string mediaExternalId, MessageType type, string? caption, bool isVoiceNote, string? messageTag, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<WhatsAppTemplateInfo>> GetApprovedTemplatesAsync(Channel channel, CancellationToken ct) => throw new NotSupportedException();
}
