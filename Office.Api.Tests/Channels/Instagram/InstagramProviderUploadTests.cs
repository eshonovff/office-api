using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Office.Api.Channels;
using Office.Api.Channels.Facebook;
using Office.Api.Channels.Instagram;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Realtime;

namespace Office.Api.Tests.Channels.Instagram;

/// <summary>
/// The upload must say what the file is. Instagram takes an image uploaded as "file" but then
/// refuses to send it (500 "Service temporarily unavailable", code 2 — checked live 2026-09-26),
/// so an image is uploaded as "image", a voice note as "audio", and so on.
/// </summary>
public class InstagramProviderUploadTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Message { get; private set; }
        public string? FileContentType { get; private set; }
        public string? Url { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.ToString();
            var form = (MultipartFormDataContent)request.Content!;
            foreach (var part in form)
            {
                var name = part.Headers.ContentDisposition?.Name?.Trim('"');
                if (name == "message")
                    Message = await part.ReadAsStringAsync(cancellationToken);
                if (name == "filedata")
                    FileContentType = part.Headers.ContentType?.MediaType;
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"attachment_id":"123456"}""", Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class PassthroughProtector : IChannelCredentialsProtector
    {
        public string Protect(string plainText) => plainText;
        public string Unprotect(string protectedText) => protectedText;
    }

    private sealed class NoOpNotificationService : INotificationService
    {
        public Task PushAsync(Guid userId, string type, object payload, CancellationToken ct) => Task.CompletedTask;
    }

    private static async Task<CapturingHandler> Upload(string mimeType)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var channel = new Channel
        {
            Id = Guid.CreateVersion7(), Type = ChannelType.Instagram, Name = "ig", ExternalId = "17841400000000000",
            CredentialsEncrypted = """{"instagramAccountId":"17841400000000000","accessToken":"tok"}""", IsActive = true,
        };
        var handler = new CapturingHandler();
        var provider = new InstagramProvider(
            new HttpClient(handler), new PassthroughProtector(), new ConfigurationBuilder().Build(), db,
            new NoOpNotificationService(), new MemoryCache(new MemoryCacheOptions()),
            new InstagramFollowCheckRateLimiter(), NullLogger<InstagramProvider>.Instance);

        var id = await provider.UploadMediaAsync(channel, new MemoryStream([1, 2, 3]), mimeType, "x", CancellationToken.None);

        Assert.Equal("123456", id);
        Assert.EndsWith("/17841400000000000/message_attachments", handler.Url);
        return handler;
    }

    /// <summary>Facebook: the same documented form (it tolerated "file", but sometimes failed with subcode 2018074).</summary>
    private static async Task<CapturingHandler> UploadToFacebook(string mimeType)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var channel = new Channel
        {
            Id = Guid.CreateVersion7(), Type = ChannelType.Facebook, Name = "fb", ExternalId = "1000",
            CredentialsEncrypted = """{"pageId":"1000","pageAccessToken":"tok"}""", IsActive = true,
        };
        var handler = new CapturingHandler();
        var provider = new FacebookProvider(
            new HttpClient(handler), new PassthroughProtector(), new ConfigurationBuilder().Build(), db,
            new NoOpNotificationService(), NullLogger<FacebookProvider>.Instance);

        Assert.Equal("123456", await provider.UploadMediaAsync(channel, new MemoryStream([1, 2, 3]), mimeType, "x", CancellationToken.None));
        Assert.EndsWith("/me/message_attachments", handler.Url);
        return handler;
    }

    [Theory]
    [InlineData("image/jpeg", "image")]
    [InlineData("video/mp4", "video")]
    [InlineData("audio/mp4", "audio")]
    [InlineData("audio/webm;codecs=opus", "audio")] // used to throw: the constructor refused a parameter
    [InlineData("application/pdf", "file")]
    public async Task Facebook_UploadsAsWhatTheFileIs(string mimeType, string expectedType)
    {
        var handler = await UploadToFacebook(mimeType);

        var attachment = JsonDocument.Parse(handler.Message!).RootElement.GetProperty("attachment");
        Assert.Equal(expectedType, attachment.GetProperty("type").GetString());
        Assert.Equal(mimeType.Split(';')[0], handler.FileContentType);
    }

    [Theory]
    [InlineData("image/jpeg", "image")]
    [InlineData("image/png", "image")]
    [InlineData("image/webp", "image")]
    [InlineData("video/mp4", "video")]
    [InlineData("audio/mp4", "audio")]
    [InlineData("audio/webm;codecs=opus", "audio")]
    [InlineData("application/pdf", "file")]
    public async Task UploadsAsWhatTheFileIs_NeverAnImageAsAFile(string mimeType, string expectedType)
    {
        var handler = await Upload(mimeType);

        var attachment = JsonDocument.Parse(handler.Message!).RootElement.GetProperty("attachment");
        Assert.Equal(expectedType, attachment.GetProperty("type").GetString());
        Assert.True(attachment.GetProperty("payload").GetProperty("is_reusable").GetBoolean());
        Assert.Equal(mimeType.Split(';')[0], handler.FileContentType); // the file keeps its own type
    }
}
