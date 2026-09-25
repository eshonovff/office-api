using System.Net;

namespace Office.Api.Channels.Flows;

/// <summary>
/// Санҷиши оддии SSRF барои action:http_request — flow-и корбар URL-ро худаш менависад, пас
/// набояд ба шабакаи дохилии сервер (localhost, IP-ҳои хусусӣ) дархост фиристода тавонад.
/// Қабати аввал; ҳимояи асосӣ (DNS, rebinding, redirect) — <see cref="SsrfSafeHttpHandler"/>.
/// </summary>
public static class HttpRequestUrlGuard
{
    /// <summary>
    /// First, cheap layer: https only, and no literal non-public address or localhost name in
    /// the URL. The real enforcement is SsrfSafeHttpHandler at connect time — a hostname that
    /// RESOLVES to a private address passes this check and is stopped there.
    /// </summary>
    public static bool IsAllowed(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return false;

        var host = uri.IdnHost.TrimEnd('.');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) || PublicAddressPolicy.IsPublic(ip);
    }
}
