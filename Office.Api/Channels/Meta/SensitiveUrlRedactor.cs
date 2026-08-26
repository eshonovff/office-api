using System.Text.RegularExpressions;

namespace Office.Api.Channels.Meta;

/// <summary>
/// URL-и пурраро (бо client_secret/access_token/code пинҳонкарда) барои лог омода мекунад —
/// pure, то диагностика (кадом host, кадом path, кадом query воқеан фиристода шуд) бе хатари
/// нишон додани сирр имконпазир бошад.
/// </summary>
public static class SensitiveUrlRedactor
{
    private static readonly string[] SensitiveKeys = ["client_secret", "access_token", "code"];

    public static string Redact(string url)
    {
        foreach (var key in SensitiveKeys)
            url = Regex.Replace(url, $"({key}=)[^&]*", "$1REDACTED");

        return url;
    }
}
