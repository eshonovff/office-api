using Office.Api.Data.Entities;

namespace Office.Api.Channels;

/// <summary>
/// Оё operator метавонад медиа/овоз ба ин канал фиристад — ҲОЗИР, на дар назария. Барои
/// Instagram/Facebook ин на аз навъи медиа вобаста аст (ниг. MediaUploadValidator барои
/// ҳудудҳо), балки аз он ки Meta Send API-и ин канал феълан кор мекунад ё не.
///
/// Санҷида зинда 2026-08-25: message_attachments (боркунии файл) барои Instagram 200
/// мегардонад, вале POST /messages бо ҳамон attachment_id ҳамеша 500 "Service temporarily
/// unavailable" медиҳад — то Meta App Review нагузарад. Матн бошад бе мушкил кор мекунад.
///
/// ЯГОНА ҷои ҳисоби ин ду байрақ — frontend (Composer) ҳамин DTO-ро мехонад, канали худро
/// hardcode намекунад, то баъд аз гузаштани App Review ЯК тағйири backend кифоя бошад, бе
/// баровардани APK-и нав.
/// </summary>
public static class ChannelCapabilities
{
    // TODO(2026-08-25, соҳиб): Facebook-и Send API исботан вайрон НАШУДААСТ — Instagram-ро
    // зинда санҷидем (боло), Facebook-ро не. Соҳиб бо қасд хост, ки то санҷиши воқеӣ Facebook
    // ҳам false монад (эҳтиёткорона), на ки бо тахмин "шояд кор мекунад" гузошта шавад. Вақте
    // касе воқеан як медиа ба Facebook бомуваффақият фиристод, ин ду метод бояд аз нав дида
    // шаванд (эҳтимол Facebook-ро аз WhatsApp-и «true» ҷудо кардан лозим намеояд дигар).
    public static bool CanSendMedia(ChannelType channelType) => channelType == ChannelType.WhatsApp;

    public static bool CanSendVoice(ChannelType channelType) => channelType == ChannelType.WhatsApp;
}
