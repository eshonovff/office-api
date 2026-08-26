using Office.Api.Data.Entities;

namespace Office.Api.Media;

public record MediaRetentionOptions(int ImageDays, int VoiceDays, int DocumentDays, int VideoDays);

/// <summary>
/// Кадом файли медиа бояд нест карда шавад — pure, бе DB/файл. Thumbnail дар ин ҷо
/// намеояд — он ҳеҷ гоҳ нест намешавад (дар MediaRetentionCleanupJob алоҳида).
/// </summary>
public static class MediaRetentionPolicy
{
    public static bool IsExpired(MessageType type, DateTimeOffset createdAt, DateTimeOffset now, MediaRetentionOptions options)
    {
        var retentionDays = RetentionDaysFor(type, options);
        if (retentionDays <= 0)
            return false;

        return now - createdAt >= TimeSpan.FromDays(retentionDays);
    }

    private static int RetentionDaysFor(MessageType type, MediaRetentionOptions options) => type switch
    {
        MessageType.Image => options.ImageDays,
        // Дар ин система "audio" ва "voice note" дар сатҳи DB фарқ надоранд (VoiceDurationSeconds ихтиёрӣ аст).
        MessageType.Audio => options.VoiceDays,
        MessageType.File => options.DocumentDays,
        MessageType.Video => options.VideoDays,
        _ => 0, // Text/Location/Contact/StoryReply — медиа надоранд
    };
}
