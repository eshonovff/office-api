using Hangfire;
using Microsoft.EntityFrameworkCore;
using Office.Api.Channels.WhatsApp;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Media;
using Office.Api.Realtime;

namespace Office.Api.Channels;

/// <summary>
/// Файли аллакай бор ба диск шудаистода (аз endpoint) — агар voice note бошад,
/// пеш аз бор ба ogg/opus transcode мешавад (webm/opus-и браузер WhatsApp-ро
/// ҳамчун voice note нишон намедиҳад) — сипас ба провайдер бор ва фиристода
/// мешавад. Дар навбати "media"-и Hangfire (маҳдуди ҳамзамонӣ) кор мекунад.
/// </summary>
[Queue("media")]
[AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 300, 1800])]
public class MediaSendJob(
    AppDbContext db,
    IChannelProviderFactory factory,
    IMediaProcessor mediaProcessor,
    IConfiguration configuration,
    IWebHostEnvironment env,
    IInboxEventPublisher events,
    ILogger<MediaSendJob> logger)
{
    public async Task SendAsync(Guid messageId, bool isVoiceNote, CancellationToken ct)
    {
        var message = await db.Messages
            .Include(m => m.Conversation).ThenInclude(c => c.Channel)
            .FirstOrDefaultAsync(m => m.Id == messageId, ct);

        if (message is null)
        {
            logger.LogWarning("MediaSendJob: паёми {MessageId} ёфт нашуд.", messageId);
            return;
        }

        var conversation = message.Conversation;
        var channel = conversation.Channel;
        var provider = factory.GetProvider(channel.Type);
        var rootPath = UploadsPathResolver.ResolveRootPath(configuration, env);

        try
        {
            if (isVoiceNote)
                await TranscodeVoiceNoteAsync(message, rootPath, ct);

            var fullPath = Path.Combine(rootPath, message.MediaUrl!);
            var mediaExternalId = await UploadAsync(provider, channel, fullPath, message, ct);

            var wamid = await provider.SendMediaMessageAsync(
                channel, conversation.ExternalId, mediaExternalId, message.Type, message.Body,
                isVoiceNote, ct);

            message.MediaExternalId = mediaExternalId;
            message.ExternalId = wamid;
            message.DeliveryStatus = MessageDeliveryStatus.Sent;
            conversation.LastMessageAt = message.CreatedAt;
        }
        catch (WhatsAppWindowClosedException ex)
        {
            message.DeliveryStatus = MessageDeliveryStatus.Failed;
            logger.LogWarning(ex, "MediaSendJob: тирезаи 24-соата баста барои паёми {MessageId}.", messageId);
        }

        await db.SaveChangesAsync(ct);
        await PublishAsync(channel.Id, conversation.AssignedTo, message, ct);
    }

    private async Task TranscodeVoiceNoteAsync(Message message, string rootPath, CancellationToken ct)
    {
        var sourceFullPath = Path.Combine(rootPath, message.MediaUrl!);
        var oggRelativePath = Path.ChangeExtension(message.MediaUrl!, ".ogg");
        var oggFullPath = Path.Combine(rootPath, oggRelativePath);

        await mediaProcessor.TranscodeToOggOpusAsync(sourceFullPath, oggFullPath, ct);
        message.VoiceDurationSeconds = await mediaProcessor.GetAudioDurationSecondsAsync(oggFullPath, ct);
        message.MimeType = "audio/ogg";
        message.MediaUrl = oggRelativePath;

        if (File.Exists(sourceFullPath))
            File.Delete(sourceFullPath);
    }

    private static async Task<string> UploadAsync(
        IChannelProvider provider, Channel channel, string fullPath, Message message, CancellationToken ct)
    {
        await using var stream = File.OpenRead(fullPath);
        return await provider.UploadMediaAsync(
            channel, stream, message.MimeType ?? "application/octet-stream",
            message.OriginalFileName ?? Path.GetFileName(fullPath), ct);
    }

    private Task PublishAsync(Guid channelId, Guid? assignedTo, Message message, CancellationToken ct)
    {
        var payload = new
        {
            message.Id,
            message.ConversationId,
            Direction = message.Direction.ToString(),
            Type = message.Type.ToString(),
            message.Body,
            message.MediaUrl,
            message.MimeType,
            message.SizeBytes,
            message.OriginalFileName,
            message.VoiceDurationSeconds,
            message.ThumbnailUrl,
            message.ExternalId,
            DeliveryStatus = message.DeliveryStatus.ToString(),
            message.CreatedAt,
        };

        return events.MessageSentAsync(channelId, assignedTo, payload, ct);
    }
}
