using System.Linq;

namespace Office.Api.Media;

/// <summary>
/// HTTP 200 танҳо маънои онро дорад, ки сервер ҷавоб дод — на он ки бадани ҷавоб воқеан медиа
/// аст. CDN-и Meta барои URL-и мӯҳлаташгузашта ё нодуруст метавонад 200 бо саҳифаи HTML-и
/// хатогӣ баргардонад (на 404/403) — маҳз ҳамин 52 файли "муваффақ"-и HTML-ро дар канали
/// Instagram сохт. Ин синф pure аст (бе DB/HTTP), то бе шабака тест шавад.
///
/// МУҲИМ (2026-08-25): пештара category-и Content-Type-ро бо навъи паём (Image→image/*,
/// Video→video/*, Audio→audio/*) сахт муқоиса мекард — вале Instagram аксар вақт овозро дар
/// контейнери video/mp4 мефиристад (на ҳамеша audio/*), ва ҳамин тавр барои видео дар
/// контейнерҳои дигар — санҷиши сахти category файлҳои комилан солимро рад мекард.
/// Ҳадафи воқеии ин синф ҳамеша пешгирии саҳифаи хатогии HTML/JSON буд (ниг. боло), на
/// маҳдуд кардани контейнер — бинобар ин акнун category-ро санҷиш намекунад, танҳо "ин бадан
/// возеҳан саҳифаи хатогӣ/матн нест" месанҷад.
/// </summary>
public static class MediaContentTypeValidator
{
    // "Явеҳан хатои сервер, на медиа" — рӯйхати кӯтоҳ бо мақсад: HTML/матн (ҳамон боги ду рӯза)
    // ва шаклҳои маъмулии ҷавоби хатогии API (Graph API-и Meta худаш JSON бармегардонад).
    private static readonly string[] RejectedExactTypes =
    [
        "application/json",
        "application/problem+json",
        "application/xml",
    ];

    public static bool Matches(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType))
            return false;

        // "; charset=utf-8" ва монанди инро бурида партофта, танҳо навъи асосиро мегирем.
        var mediaType = contentType.Split(';')[0].Trim();

        if (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            return false;

        return !RejectedExactTypes.Contains(mediaType, StringComparer.OrdinalIgnoreCase);
    }
}
