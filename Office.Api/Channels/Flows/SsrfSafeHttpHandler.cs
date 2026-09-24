using System.Net;
using System.Net.Sockets;

namespace Office.Api.Channels.Flows;

/// <summary>
/// Which addresses a flow's http_request may reach. Flows are now written by мизоҷон — untrusted
/// users — so the server must never be usable as a door into its own network.
/// </summary>
public static class PublicAddressPolicy
{
    public static bool IsPublic(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        var b = ip.GetAddressBytes();

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            return !(
                b[0] == 0                                         // 0.0.0.0/8 — 0.0.0.0 reaches localhost on Linux
                || b[0] == 10                                     // 10.0.0.0/8
                || b[0] == 127                                    // loopback
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)     // 100.64.0.0/10 carrier-grade NAT
                || (b[0] == 169 && b[1] == 254)                   // link-local, cloud metadata (169.254.169.254)
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)      // 172.16.0.0/12
                || (b[0] == 192 && b[1] == 0 && b[2] is 0 or 2)   // 192.0.0.0/24 IETF, TEST-NET-1
                || (b[0] == 192 && b[1] == 168)                   // 192.168.0.0/16
                || (b[0] == 198 && b[1] is 18 or 19)              // benchmarking
                || (b[0] == 198 && b[1] == 51 && b[2] == 100)     // TEST-NET-2
                || (b[0] == 203 && b[1] == 0 && b[2] == 113)      // TEST-NET-3
                || b[0] >= 224);                                  // multicast, reserved, broadcast
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // ::/96 — the unspecified address, loopback (::1) and deprecated IPv4-compatible forms.
            if (b.Take(12).All(x => x == 0))
                return false;

            // 64:ff9b::/96 NAT64: the real destination is the embedded IPv4 address.
            if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b && b.Skip(4).Take(8).All(x => x == 0))
                return IsPublic(new IPAddress(b[12..16]));

            return !(
                ip.IsIPv6LinkLocal                                // fe80::/10
                || ip.IsIPv6SiteLocal                             // fec0::/10
                || ip.IsIPv6Multicast                             // ff00::/8
                || (b[0] & 0xFE) == 0xFC                          // fc00::/7 unique local
                || (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8)); // 2001:db8::/32 documentation
        }

        return false;
    }
}

/// <summary>
/// The HTTP handler behind a flow's http_request. The URL check alone (HttpRequestUrlGuard) only
/// sees what is written in the URL; a hostname can resolve to a private address, or resolve
/// differently between the check and the connection (DNS rebinding). So the decision is made
/// here, at connect time, on the exact addresses the socket connects to.
/// </summary>
public static class SsrfSafeHttpHandler
{
    public static SocketsHttpHandler Create() => new()
    {
        ConnectCallback = ConnectAsync,
        // A redirect is a second URL nobody vetted; a proxy would resolve the target itself,
        // out of this handler's reach.
        AllowAutoRedirect = false,
        UseProxy = false,
        ConnectTimeout = TimeSpan.FromSeconds(10),
    };

    /// <summary>
    /// Every address the host resolves to must be public — if any is not, the whole request is
    /// refused (a mix of public and private records is a classic way to slip one through).
    /// </summary>
    public static async Task<IPAddress[]> ResolvePublicAddressesAsync(string host, CancellationToken ct)
    {
        var addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, ct);

        if (addresses.Length == 0 || !addresses.All(PublicAddressPolicy.IsPublic))
            throw new HttpRequestException($"Дархост ба '{host}' манъ аст: суроғаи ғайриҷамъиятӣ.");

        return addresses;
    }

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var addresses = await ResolvePublicAddressesAsync(context.DnsEndPoint.Host, ct);

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
