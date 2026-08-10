using Office.Api.Data.Entities;

namespace Office.Api.Media;

/// <summary>Ҳудуди ҳаҷми файл барои WhatsApp: расм 5 МБ, овоз/видео 16 МБ, ҳуҷҷат 100 МБ.</summary>
public static class MediaUploadValidator
{
    public const long ImageMaxBytes = 5 * 1024 * 1024;
    public const long AudioVideoMaxBytes = 16 * 1024 * 1024;
    public const long DocumentMaxBytes = 100 * 1024 * 1024;

    public static (MessageType Type, long MaxSizeBytes) Classify(string mimeType)
    {
        if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return (MessageType.Image, ImageMaxBytes);

        if (mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            return (MessageType.Video, AudioVideoMaxBytes);

        if (mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            return (MessageType.Audio, AudioVideoMaxBytes);

        return (MessageType.File, DocumentMaxBytes);
    }

    public static bool IsWithinLimit(string mimeType, long sizeBytes)
    {
        var (_, maxSizeBytes) = Classify(mimeType);
        return sizeBytes is > 0 && sizeBytes <= maxSizeBytes;
    }
}
