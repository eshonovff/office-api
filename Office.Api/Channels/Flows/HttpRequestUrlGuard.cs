using System.Net;

namespace Office.Api.Channels.Flows;

/// <summary>
/// Санҷиши оддии SSRF барои action:http_request — flow-и корбар URL-ро худаш менависад, пас
/// набояд ба шабакаи дохилии сервер (localhost, IP-ҳои хусусӣ) дархост фиристода тавонад.
/// Ин ҳимояи "оддӣ" аст (спека: "маҳдудияти оддии бехатарӣ"), на DNS-rebinding-пурра.
/// </summary>
public static class HttpRequestUrlGuard
{
    public static bool IsAllowed(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return false;

        if (!IPAddress.TryParse(uri.Host, out var ip))
            return !string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);

        var bytes = ip.GetAddressBytes();
        if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return !IPAddress.IsLoopback(ip);

        return bytes[0] switch
        {
            127 => false, // loopback
            10 => false, // 10.0.0.0/8
            192 when bytes[1] == 168 => false, // 192.168.0.0/16
            169 when bytes[1] == 254 => false, // link-local
            172 when bytes[1] is >= 16 and <= 31 => false, // 172.16.0.0/12
            _ => true,
        };
    }
}
