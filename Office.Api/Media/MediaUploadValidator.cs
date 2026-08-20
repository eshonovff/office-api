using Office.Api.Data.Entities;

namespace Office.Api.Media;

/// <summary>
/// Ҳудуди андозаи файл — вобаста ба навъи канал, чунки WhatsApp ва Messenger Platform
/// (Facebook/Instagram) ду системаи гуногунанд бо ҳудуди гуногун:
///
/// - WhatsApp Cloud API-и худи Meta: расм 5 МБ, овоз/видео 16 МБ, ҳуҷҷат 100 МБ
///   (алоҳида барои ҳар навъ). Санҷида 2026-08-19 аз рӯи мустанадоти расмии WhatsApp
///   Cloud API — https://developers.facebook.com/docs/whatsapp/cloud-api/reference/media
/// - Messenger Platform (Facebook Messenger ва Instagram Messaging, ҳарду ҳамон Send/
///   Attachment Upload API-ро истифода мебаранд): ҳудуди ЯГОНА барои ҳар навъи attachment —
///   на алоҳида барои расм/видео/ҳуҷҷат. Санҷида 2026-08-19 аз рӯи мустанадоти расмии
///   Messenger Platform (Attachment Upload API — 25 МБ). Meta метавонад ин рақамҳоро
///   тағйир диҳад — агар хатогии 413/rate-limit аз тарафи Meta бештар шавад, инҷо аввал санҷед.
///
/// GET /api/conversations/{id} ин ҳудудҳоро (MediaLimits, ниг. ConversationDetail) ба
/// frontend мефиристад, то он ҳамон рақамҳоро пеш аз боркунӣ санҷад — ду ҷои алоҳидаи
/// нигоҳдории як маълумот набояд вуҷуд дошта бошад.
/// </summary>
public static class MediaUploadValidator
{
    public const long ImageMaxBytes = 5 * 1024 * 1024;
    public const long AudioVideoMaxBytes = 16 * 1024 * 1024;
    public const long DocumentMaxBytes = 100 * 1024 * 1024;

    public const long MessengerAttachmentMaxBytes = 25 * 1024 * 1024;

    public static (MessageType Type, long MaxSizeBytes) Classify(ChannelType channelType, string mimeType)
    {
        var type = ClassifyType(mimeType);
        return (type, MaxBytesFor(channelType, type));
    }

    public static bool IsWithinLimit(ChannelType channelType, string mimeType, long sizeBytes)
    {
        var (_, maxSizeBytes) = Classify(channelType, mimeType);
        return sizeBytes is > 0 && sizeBytes <= maxSizeBytes;
    }

    /// <summary>Пурраи ҷадвали ҳудуд барои як навъи канал — GET /api/conversations/{id}-и ConversationDetail.MediaLimits ҳамин рӯйхатро мебарорад.</summary>
    public static IReadOnlyList<MediaTypeLimit> LimitsFor(ChannelType channelType) =>
    [
        new("image", MaxBytesFor(channelType, MessageType.Image)),
        new("audioVideo", MaxBytesFor(channelType, MessageType.Audio)),
        new("document", MaxBytesFor(channelType, MessageType.File)),
    ];

    private static MessageType ClassifyType(string mimeType)
    {
        if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return MessageType.Image;

        if (mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            return MessageType.Video;

        if (mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            return MessageType.Audio;

        return MessageType.File;
    }

    private static long MaxBytesFor(ChannelType channelType, MessageType type)
    {
        if (channelType != ChannelType.WhatsApp)
            return MessengerAttachmentMaxBytes;

        return type switch
        {
            MessageType.Image => ImageMaxBytes,
            MessageType.Audio or MessageType.Video => AudioVideoMaxBytes,
            _ => DocumentMaxBytes,
        };
    }
}

/// <summary>Ҳудуди як категорияи файл (расм/аудио-видео/ҳуҷҷат) барои канали мушаххас.</summary>
public record MediaTypeLimit(string Category, long MaxSizeBytes);
