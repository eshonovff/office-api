using Hangfire;
using Microsoft.EntityFrameworkCore;
using Office.Api.Channels.Messenger;
using Office.Api.Channels.WhatsApp;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Features.Conversations;
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
        // SentByUserName is a persisted snapshot on Message itself — no need to
        // Include(SentByUser) just to resolve the display name for FromEntity below.
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

        // Ин канал ҳозир ин навъи файлро тамоман намефиристад (ниг. ChannelCapabilities — барои
        // Instagram/Facebook феълан ҳамеша) — санҷиши ин ҷо, ПЕШ АЗ ҳар кӯшиши воқеӣ, кофтуков ба
        // Meta-и бефоидаро (ва 3 такрори Hangfire-и он) пешгирӣ мекунад, хатои возеҳ фавран медиҳад.
        var canSendThisType = isVoiceNote ? ChannelCapabilities.CanSendVoice(channel.Type) : ChannelCapabilities.CanSendMedia(channel.Type);
        if (!canSendThisType)
        {
            message.DeliveryStatus = MessageDeliveryStatus.Failed;
            message.FailureReason = isVoiceNote
                ? $"{channel.Type}: фиристодани voice note ҳозир дастрас нест."
                : $"{channel.Type}: фиристодани медиа ҳозир дастрас нест.";
            logger.LogWarning(
                "MediaSendJob: {ChannelType} метавонад {Kind} нафиристад — паёми {MessageId} рад шуд, ба Meta намерасонам.",
                channel.Type, isVoiceNote ? "voice note" : "медиа", messageId);
            await db.SaveChangesAsync(ct);
            await PublishAsync(channel.Id, conversation.AssignedTo, message, ct);
            return;
        }

        var provider = factory.GetProvider(channel.Type);
        var rootPath = UploadsPathResolver.ResolveRootPath(configuration, env);

        // Facebook/Instagram: тиреза/тег пеш аз боркунӣ санҷида мешавад — агар Reject (7 рӯз
        // гузаштааст), боркунии бефоида намешавад. WhatsApp (поён) ин санҷишро надорад — он ба
        // хатои реактивии WhatsAppWindowClosedException-и провайдер такя мекунад (тағйирнаёфта).
        string? messageTag = null;
        if (channel.Type is ChannelType.Facebook or ChannelType.Instagram)
        {
            var mode = MessengerSendModePlanner.Plan(conversation.WindowExpiresAt, DateTimeOffset.UtcNow);
            if (mode == MessengerSendMode.Reject)
            {
                message.DeliveryStatus = MessageDeliveryStatus.Failed;
                message.FailureReason = "Тирезаи 24-соат ва дарозкунии 7-рӯзаи тег (HUMAN_AGENT) ҳарду гузаштаанд.";
                await db.SaveChangesAsync(ct);
                await PublishAsync(channel.Id, conversation.AssignedTo, message, ct);
                return;
            }

            messageTag = mode == MessengerSendMode.Tag ? MessengerTags.HumanAgent : null;
        }

        try
        {
            // МУҲИМ (2026-08-25, ниг. report): пеш аз ин, агар TranscodeVoiceNoteAsync муваффақ
            // мешуд (.webm нест карда мешуд, MediaUrl → .ogg дар ХОТИРА иваз мешуд) вале қадами
            // БАЪДӢ (боркунӣ/фиристодан ба Meta) хато медод, SaveChangesAsync ҳеҷ гоҳ намерасид —
            // тағйирот гум мешуд. Кӯшиши такрори Hangfire (баъди хатои муваққатии Meta, масалан
            // "Service temporarily unavailable") message.MediaUrl-ро БОЗ ".webm" медид ва
            // TranscodeVoiceNoteAsync-ро АЗ НАВ даъват мекард — вале файли манбаъ аллакай нест шуда
            // буд → ffmpeg "No such file or directory" абадан. Санҷиши MimeType-и поён + SaveChanges
            // фавран пас аз transcode ин ҳолатро пешгирӣ мекунад: кӯшиши дуюм онро дубора намекунад.
            // МУҲИМ (2026-08-25, ниг. report): Facebook/Instagram-и message_attachments audio/ogg
            // (Opus)-ро намепазирад — Meta docs-и худашон танҳо aac/m4a/wav/mp4-ро номбар мекунад;
            // санҷиши зинда инро тасдиқ кард (OAuthException, code 1, "An unknown error has
            // occurred" — хатои норавшан, на возеҳ). WhatsApp баръакс: ogg/opus-ро ҳамчун voice
            // note интизор аст (документатсияи расмии Cloud API). Ду формат, ду роҳ.
            var targetMimeType = channel.Type is ChannelType.Facebook or ChannelType.Instagram ? "audio/mp4" : "audio/ogg";
            if (isVoiceNote && message.MimeType != targetMimeType)
            {
                await TranscodeVoiceNoteAsync(message, channel.Type, rootPath, ct);
                await db.SaveChangesAsync(ct);
            }

            var fullPath = Path.Combine(rootPath, message.MediaUrl!);

            // Ҳамон вазифае, ки MediaDownloadJob барои Audio-и воридотӣ иҷро мекунад —
            // вагарна пикҳои овозҳои худи мо (на мижоз) намоиш дода намешаванд.
            if (message.Type == MessageType.Audio)
                message.WaveformPeaks = await TryGenerateWaveformPeaksAsync(fullPath, message.Id, ct);

            var mediaExternalId = await UploadAsync(provider, channel, fullPath, message, ct);

            var wamid = await provider.SendMediaMessageAsync(
                channel, conversation.ExternalId, mediaExternalId, message.Type, message.Body,
                isVoiceNote, messageTag, ct);

            message.MediaExternalId = mediaExternalId;
            message.ExternalId = wamid;
            message.DeliveryStatus = MessageDeliveryStatus.Sent;
            message.FailureReason = null;
            conversation.LastMessageAt = message.CreatedAt;
        }
        catch (WhatsAppWindowClosedException ex)
        {
            message.DeliveryStatus = MessageDeliveryStatus.Failed;
            message.FailureReason = "Тирезаи 24-соата баста аст — танҳо шаблон фиристода мешавад.";
            logger.LogWarning(ex, "MediaSendJob: тирезаи 24-соата баста барои паёми {MessageId}.", messageId);
            await db.SaveChangesAsync(ct);
            await PublishAsync(channel.Id, conversation.AssignedTo, message, ct);
            return;
        }
        catch (GraphApiException ex)
        {
            // ex.Message аллакай тарҷумашудааст (MetaErrorTranslator, дар провайдер) — на JSON-и
            // хом. RawResponseBody фақат барои debug (FailureDetail → frontend-и пӯшида) захира
            // мешавад, ҳеҷ гоҳ дар ҳубоб мустақим чоп намешавад.
            message.DeliveryStatus = MessageDeliveryStatus.Failed;
            message.FailureReason = ex.Message;
            message.FailureDetail = ex.RawResponseBody.Length > 4000 ? ex.RawResponseBody[..4000] : ex.RawResponseBody;
            message.FailureCode = MetaErrorCodeExtractor.Extract(channel.Type, ex.RawResponseBody);
            // А2: коди ношинос — то дафъаи оянда маълумот дошта бошем (на боз "як маротиба").
            if (message.FailureCode is not null && !FailureCodeLabels.IsKnown(message.FailureCode))
                logger.LogWarning("MediaSendJob: failure_code ношинос {FailureCode} — payload: {RawResponseBody}", message.FailureCode, ex.RawResponseBody);
            logger.LogError(ex, "MediaSendJob: Meta Graph API рад кард — паёми {MessageId}.", messageId);
            await db.SaveChangesAsync(ct);
            await PublishAsync(channel.Id, conversation.AssignedTo, message, ct);
            throw;
        }
        catch (Exception ex)
        {
            // Пеш аз ин: хар хатои дигар (аз Meta ё аз ҷои дигар) хомӯшона партофта мешуд —
            // Hangfire дар паси парда такрор мекард, вале паём то анҷоми ҳамаи кӯшишҳо "Pending"
            // мемонд, бе ҳеҷ нишонае дар UI, ки чизе вайрон шуд (ниг. report: ҳам сурат, ҳам овоз
            // "абадан фиристода мешуда" менамуданд, бе хатои возеҳ). Акнун ҳар кӯшиши ноком фавран
            // "Failed"+сабаби воқеиро нишон медиҳад; агар кӯшиши оянда муваффақ шавад, ба "Sent"
            // бармегардад (боло). throw — Hangfire мувофиқи [AutomaticRetry] такрор мекунад.
            message.DeliveryStatus = MessageDeliveryStatus.Failed;
            message.FailureReason = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            logger.LogError(ex, "MediaSendJob: кӯшиши фиристодани паёми {MessageId} ноком шуд.", messageId);
            await db.SaveChangesAsync(ct);
            await PublishAsync(channel.Id, conversation.AssignedTo, message, ct);
            throw;
        }

        await db.SaveChangesAsync(ct);
        await PublishAsync(channel.Id, conversation.AssignedTo, message, ct);
    }

    private async Task TranscodeVoiceNoteAsync(Message message, ChannelType channelType, string rootPath, CancellationToken ct)
    {
        var sourceFullPath = Path.Combine(rootPath, message.MediaUrl!);
        // aac/m4a барои Facebook/Instagram (ogg/opus рад мешавад — ниг. SendAsync), ogg/opus барои WhatsApp.
        var useAac = channelType is ChannelType.Facebook or ChannelType.Instagram;
        var targetRelativePath = Path.ChangeExtension(message.MediaUrl!, useAac ? ".m4a" : ".ogg");
        var targetFullPath = Path.Combine(rootPath, targetRelativePath);

        if (useAac)
            await mediaProcessor.TranscodeToAacAsync(sourceFullPath, targetFullPath, ct);
        else
            await mediaProcessor.TranscodeToOggOpusAsync(sourceFullPath, targetFullPath, ct);

        message.VoiceDurationSeconds = await mediaProcessor.GetAudioDurationSecondsAsync(targetFullPath, ct);
        message.MimeType = useAac ? "audio/mp4" : "audio/ogg";
        message.MediaUrl = targetRelativePath;

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

    private Task PublishAsync(Guid channelId, Guid? assignedTo, Message message, CancellationToken ct) =>
        events.MessageSentAsync(channelId, assignedTo, MessageDto.FromEntity(message), ct);
}
