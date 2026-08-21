using Office.Api.Data.Entities;

namespace Office.Api.Media;

/// <summary>
/// HTTP 200 танҳо маънои онро дорад, ки сервер ҷавоб дод — на он ки бадани ҷавоб воқеан медиа
/// аст. CDN-и Meta барои URL-и мӯҳлаташгузашта ё нодуруст метавонад 200 бо саҳифаи HTML-и
/// хатогӣ баргардонад (на 404/403) — маҳз ҳамин 52 файли "муваффақ"-и HTML-ро дар канали
/// Instagram сохт. Ин синф pure аст (бе DB/HTTP), то бе шабака тест шавад.
/// </summary>
public static class MediaContentTypeValidator
{
    public static bool Matches(MessageType expectedType, string? contentType)
    {
        if (string.IsNullOrEmpty(contentType))
            return false;

        // "; charset=utf-8" ва монанди инро бурида партофта, танҳо навъи асосиро мегирем.
        var mediaType = contentType.Split(';')[0].Trim();

        return expectedType switch
        {
            MessageType.Image => mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase),
            MessageType.Video => mediaType.StartsWith("video/", StringComparison.OrdinalIgnoreCase),
            MessageType.Audio => mediaType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase),
            // Ҳуҷҷат: рӯйхати мушаххас нест (PDF, Word, ZIP, ...) — вале ҳеҷ гоҳ text/* нест,
            // маҳз ҳамин "text/html" буд, ки хомӯшона гум мешуд.
            MessageType.File => !mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase),
            // StoryReply/Location/Contact — ин ҷо намерасанд (медиа-даунлоуд надоранд ё роҳи дигар доранд).
            _ => true,
        };
    }
}
