using Office.Api.Data.Entities;

namespace Office.Api.Channels;

/// <summary>
/// Оё operator метавонад медиа/овоз ба ин канал фиристад — ҲОЗИР, на дар назария. Барои
/// Instagram/Facebook ин на аз навъи медиа вобаста аст (ниг. MediaUploadValidator барои
/// ҳудудҳо), балки аз он ки Meta Send API-и ин канал феълан кор мекунад ё не.
///
/// Instagram (санҷида зинда 2026-08-25): message_attachments 200 мегардонад, вале POST
/// /messages бо ҳамон attachment_id ҲАМЕША 500 "Service temporarily unavailable" (code 2,
/// is_transient) медиҳад. Он вақт гумон шуд, ки сабаб App Review аст — **нодуруст.** Сабаби
/// аслӣ (санҷида зинда 2026-09-26): InstagramProvider.UploadMediaAsync ҳама чизро ҳамчун "file"
/// бор мекард. Бо навъи дуруст ҳама расиданд: сурат (106 KB, 1.2 MB), видео (mp4), овоз (m4a —
/// ҳамон формате, ки MediaSendJob месозад), PDF. WEBP-ро Instagram рад мекунад (500, code 1) —
/// MediaSendJob онро пеш аз боркунӣ ба JPEG табдил медиҳад. Аз ин рӯ Instagram — true.
///
/// Facebook (санҷида зинда 2026-08-26, ниг. report): 3 паёми ноком дар DB (subcode 2018074,
/// "Не удалось скачать вложение с помощью его ID") бо скрипти мустақил (бе Hangfire, ҳамон
/// токени production) 5 маротиба АЙНАН такрор карда шуд — сурати хурд, audio/mp4-и ба
/// андозаи voice note-ҳои воқеӣ (~26-40KB), PNG-и калон (~1.4MB, наздик ба 2.1MB-и ноком).
/// Ҳар панҷ маротиба message_attachments ва баъд POST /messages ҳарду 200 доданд — механизм
/// (multipart bytes → attachment_id → /messages) 100% дуруст кор мекунад. Ду аз се хатои
/// DB-ӣ ҳамагӣ 3 сония аз ҳам дур буданд (14:21:48 ва 14:21:51) — аломати як ҳодисаи
/// муваққатии тарафи Meta, на баг дар код ё маҳдудияти навъ/андозаи файл. MediaSendJob-и
/// мавҷуда аллакай AutomaticRetry дорад — хатои муваққатии оянда худкор такрор мешавад.
///
/// ЯГОНА ҷои ҳисоби ин ду байрақ — frontend (Composer) ҳамин DTO-ро мехонад, канали худро
/// hardcode намекунад, то баъд аз гузаштани App Review-и Instagram ЯК тағйири backend кифоя
/// бошад, бе баровардани APK-и нав.
/// </summary>
public static class ChannelCapabilities
{
    public static bool CanSendMedia(ChannelType channelType) =>
        channelType is ChannelType.WhatsApp or ChannelType.Facebook or ChannelType.Instagram;

    public static bool CanSendVoice(ChannelType channelType) =>
        channelType is ChannelType.WhatsApp or ChannelType.Facebook or ChannelType.Instagram;
}
