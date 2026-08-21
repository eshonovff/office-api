using Hangfire;
using Microsoft.EntityFrameworkCore;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Features.Conversations;
using Office.Api.Media;
using Office.Api.Realtime;

namespace Office.Api.Channels;

/// <summary>
/// Media id-и WhatsApp пас аз 5 дақиқа эътибор надорад ва Meta файлро баъди 30 рӯз
/// нест мекунад — бинобар ин боркунӣ бояд боэътимод бошад: такрор бо backoff,
/// хатогӣ дар <see cref="Message.MediaDownloadError"/> сабт мешавад (хомӯшона гум намешавад).
/// Дар навбати "media"-и Hangfire (маҳдуди ҳамзамонӣ) кор мекунад.
/// </summary>
[Queue("media")]
[AutomaticRetry(Attempts = 5, DelaysInSeconds = [30, 300, 1800, 7200, 21600])]
public class MediaDownloadJob(
    AppDbContext db,
    IChannelProviderFactory factory,
    IMediaProcessor mediaProcessor,
    IConfiguration configuration,
    IWebHostEnvironment env,
    IInboxEventPublisher events,
    ILogger<MediaDownloadJob> logger)
{
    private const int ThumbnailMaxDimension = 320;

    public async Task DownloadAsync(Guid messageId, string mediaExternalId, CancellationToken ct)
    {
        var message = await db.Messages
            .Include(m => m.Conversation).ThenInclude(c => c.Channel)
            .FirstOrDefaultAsync(m => m.Id == messageId, ct);

        if (message is null)
        {
            logger.LogWarning("MediaDownloadJob: паёми {MessageId} ёфт нашуд.", messageId);
            return;
        }

        var channel = message.Conversation.Channel;
        var provider = factory.GetProvider(channel.Type);

        try
        {
            var downloaded = await provider.DownloadMediaAsync(channel, mediaExternalId, ct);
            await using var stream = downloaded.Content;

            // HTTP 200 танҳо маънои "сервер ҷавоб дод" дорад — на он ки бадан воқеан медиа аст.
            // Ин маҳз он ҷоест, ки CDN-и Meta барои URL-и мӯҳлаташгузашта 200+HTML баргардонд ва
            // ҳамчун "муваффақ" сабт шуд. Диски бе фоида нависонда намешавад — ин хатои НИҲОӢ аст
            // (URL ҳеҷ гоҳ дигар намешавад), пас такрор (throw поён) фоида надорад — return мекунем.
            if (!MediaContentTypeValidator.Matches(message.Type, downloaded.ContentType))
            {
                var redactedId = MediaLogRedactor.Redact(mediaExternalId);
                logger.LogError(
                    "Media {MediaExternalId} барои паёми {MessageId}: Content-Type '{ContentType}' ба навъи интизории {ExpectedType} мувофиқ намеояд — эҳтимол URL мӯҳлаташ гузаштааст ё нодуруст аст.",
                    redactedId, message.Id, downloaded.ContentType ?? "(холӣ)", message.Type);

                message.MediaDownloadError =
                    $"Сервер бадани '{downloaded.ContentType ?? "(бе Content-Type)"}' баргардонд, на медиаи воқеӣ — URL эҳтимол мӯҳлаташ гузаштааст.";
                await db.SaveChangesAsync(ct);
                await PublishAsync(message, ct);
                return;
            }

            var mediaFolder = Path.Combine(UploadsPathResolver.ResolveRootPath(configuration, env), "whatsapp-media", channel.Id.ToString());
            Directory.CreateDirectory(mediaFolder);

            var storedFileName = Guid.CreateVersion7().ToString();
            var fullPath = Path.Combine(mediaFolder, storedFileName);

            long sizeBytes;
            await using (var fileStream = File.Create(fullPath))
            {
                await stream.CopyToAsync(fileStream, ct);
                sizeBytes = fileStream.Length;
            }

            message.MediaUrl = Path.Combine("whatsapp-media", channel.Id.ToString(), storedFileName);
            message.SizeBytes = sizeBytes;
            message.MediaExternalId = mediaExternalId;
            message.MediaDownloadError = null;
            // Facebook/Instagram намедиҳанд mime_type дар webhook (WhatsApp медиҳад) — Content-Type-и
            // ҳамин боркунӣ акнун сарчашмаи воқеӣ аст, на application/octet-stream-и пешфарз, ки <video>
            // бозӣ намекард.
            message.MimeType = downloaded.ContentType;

            if (message.Type == MessageType.Image)
                message.ThumbnailUrl = await TryGenerateThumbnailAsync(fullPath, mediaFolder, channel.Id, message.Id, ct);

            if (message.Type == MessageType.Audio)
            {
                message.VoiceDurationSeconds = await TryProbeDurationAsync(fullPath, message.Id, ct);
                message.WaveformPeaks = await TryGenerateWaveformPeaksAsync(fullPath, message.Id, ct);
            }

            await db.SaveChangesAsync(ct);
            await PublishAsync(message, ct);
        }
        catch (Exception ex)
        {
            // Facebook/Instagram дар mediaExternalId URL-и пурраи CDN (бо токени дастрасӣ дар
            // query) мегузоранд, на ID-и мубҳами WhatsApp — MediaLogRedactor query-ро мебурад.
            logger.LogError(
                ex, "Нусхабардории media {MediaExternalId} барои паёми {MessageId} ноком шуд",
                MediaLogRedactor.Redact(mediaExternalId), message.Id);

            message.MediaDownloadError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
            await db.SaveChangesAsync(ct);
            await PublishAsync(message, ct);

            throw; // AutomaticRetry-и Hangfire бо backoff такрор мекунад; агар кӯшишҳо тамом шаванд, хатогии дар боло сабтшуда мемонад.
        }
    }

    /// <summary>
    /// MessageReceived-и вебҳук бо ҳолати ПЕШ аз боркунӣ мефиристад (MediaUrl холӣ) —
    /// бе ин, клиент ҳеҷ гоҳ намедонад, ки медиа (ва thumbnail/давомнокӣ) омода шуд ё
    /// боркунӣ ноком шуд. Ҳамон номи event (MessageReceived) — паёми нав нест, танҳо
    /// навсозии ҳамон паём, тибқи алгуи такрористифодаи event-ҳои мавҷуда.
    /// </summary>
    private Task PublishAsync(Message message, CancellationToken ct) =>
        events.MessageReceivedAsync(
            message.Conversation.ChannelId, message.Conversation.AssignedTo, MessageDto.FromEntity(message), ct);

    private async Task<string?> TryGenerateThumbnailAsync(string fullPath, string mediaFolder, Guid channelId, Guid messageId, CancellationToken ct)
    {
        try
        {
            var thumbFileName = $"{Path.GetFileNameWithoutExtension(fullPath)}_thumb.jpg";
            var thumbFullPath = Path.Combine(mediaFolder, thumbFileName);
            await mediaProcessor.GenerateImageThumbnailAsync(fullPath, thumbFullPath, ThumbnailMaxDimension, ct);
            return Path.Combine("whatsapp-media", channelId.ToString(), thumbFileName);
        }
        catch (MediaProcessingException ex)
        {
            logger.LogWarning(ex, "Сохтани thumbnail барои паёми {MessageId} ноком шуд", messageId);
            return null;
        }
    }

    private async Task<int?> TryProbeDurationAsync(string fullPath, Guid messageId, CancellationToken ct)
    {
        try
        {
            return await mediaProcessor.GetAudioDurationSecondsAsync(fullPath, ct);
        }
        catch (MediaProcessingException ex)
        {
            logger.LogWarning(ex, "Пурсиши давомнокии овоз барои паёми {MessageId} ноком шуд", messageId);
            return null;
        }
    }

    private async Task<short[]?> TryGenerateWaveformPeaksAsync(string fullPath, Guid messageId, CancellationToken ct)
    {
        try
        {
            return [.. await mediaProcessor.GenerateWaveformPeaksAsync(fullPath, WaveformPeakCalculator.DefaultPeakCount, ct)];
        }
        catch (MediaProcessingException ex)
        {
            logger.LogWarning(ex, "Сохтани пикҳои шакли мавҷ барои паёми {MessageId} ноком шуд", messageId);
            return null;
        }
    }
}
