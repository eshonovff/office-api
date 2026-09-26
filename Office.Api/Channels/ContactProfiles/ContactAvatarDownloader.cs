using Office.Api.Common;
using Office.Api.Media;

namespace Office.Api.Channels.ContactProfiles;

/// <summary>
/// Fetches a contact's picture from Meta's CDN and keeps a small copy of our own (a 128 px JPEG):
/// Meta's links stop working after ~4 days. Only https links to Meta's own CDN are fetched, a
/// redirect only to one of them too (the link comes from Meta, but a picture download must never
/// be a way to reach anything else), at most 5 MB in 15 s; ffmpeg — reading only picture formats —
/// makes the copy, so a file that is not really a picture stops there.
/// </summary>
public class ContactAvatarDownloader(HttpClient http, IMediaProcessor mediaProcessor, ILogger<ContactAvatarDownloader> logger)
{
    public const long MaxBytes = 5 * 1024 * 1024;
    public const int SizePx = 128;
    private const int MaxRedirects = 3;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private static readonly string[] MetaCdnHosts = [".cdninstagram.com", ".fbcdn.net", ".fbsbx.com"];

    public static bool IsMetaCdn(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) &&
        MetaCdnHosts.Any(host => uri.Host.EndsWith(host, StringComparison.OrdinalIgnoreCase));

    /// <returns>The copy, relative to the uploads root (in the channel's own media folder); null when there is none.</returns>
    public async Task<string?> SaveAsync(string sourceUrl, Guid channelId, Guid conversationId, string uploadsRoot, CancellationToken ct)
    {
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) || !IsMetaCdn(uri))
        {
            logger.LogWarning("Сурати контакти {ConversationId} гирифта нашуд: пайванд аз CDN-и Meta нест.", conversationId);
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        var temp = Path.Combine(Path.GetTempPath(), $"avatar-{Guid.NewGuid()}.src");
        try
        {
            using var response = await GetFollowingMetaRedirectsAsync(uri, conversationId, timeout.Token);
            if (response is null || !response.IsSuccessStatusCode ||
                response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true ||
                response.Content.Headers.ContentLength > MaxBytes)
                return null;

            await using (var file = File.Create(temp))
            await using (var body = await response.Content.ReadAsStreamAsync(timeout.Token))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await body.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    total += read;
                    if (total > MaxBytes)
                        return null;
                    await file.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                }
            }

            var relative = Path.Combine("whatsapp-media", channelId.ToString(), "avatars",
                $"{conversationId}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.jpg");
            var full = SafeUploadsPath.TryResolve(uploadsRoot, relative);
            if (full is null)
                return null;
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await mediaProcessor.GenerateImageThumbnailAsync(temp, full, SizePx, timeout.Token);
            return relative;
        }
        catch (Exception ex) when (ex is HttpRequestException or MediaProcessingException ||
                                   ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Сурати контакти {ConversationId} гирифта нашуд: {Reason}", conversationId, ex.GetType().Name);
            return null;
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    private async Task<HttpResponseMessage?> GetFollowingMetaRedirectsAsync(Uri uri, Guid conversationId, CancellationToken ct)
    {
        for (var hop = 0; ; hop++)
        {
            var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            var status = (int)response.StatusCode;
            if (status is < 300 or >= 400)
                return response;

            var location = response.Headers.Location;
            response.Dispose();
            if (location is null || hop >= MaxRedirects)
                return null;
            uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
            if (!IsMetaCdn(uri))
            {
                logger.LogWarning("Сурати контакти {ConversationId}: redirect берун аз CDN-и Meta — рад шуд.", conversationId);
                return null;
            }
        }
    }
}
