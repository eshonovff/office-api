namespace Office.Api.Channels;

/// <summary>
/// Барои лог: WhatsApp-и mediaExternalId як ID-и мубҳам аст (бехатар), вале Facebook/Instagram
/// дар ин ҷо URL-и пурраи CDN мегузоранд (бо токени дастрасӣ дар query string — на ID). Ин
/// URL ҳеҷ гоҳ набояд пурра ба лог равад: query-ро мебурад, танҳо scheme+host+path мемонад —
/// кофист барои ташхис (кадом CDN, кадом роҳ), бе ошкор кардани токен.
/// </summary>
public static class MediaLogRedactor
{
    public static string Redact(string mediaExternalId)
    {
        if (!Uri.TryCreate(mediaExternalId, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            // ID-и мубҳам (WhatsApp-монанд) — URL нест, пурра бехатар аст.
            return mediaExternalId;
        }

        return $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath} (query бурида шуд)";
    }
}
