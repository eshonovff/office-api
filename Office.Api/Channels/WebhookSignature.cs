using System.Security.Cryptography;
using System.Text;

namespace Office.Api.Channels;

/// <summary>
/// Санҷиши имзои `X-Hub-Signature-256`-и Meta (HMAC-SHA256 бо App Secret).
/// Ин алгоритм барои ҳамаи маҳсулоти Meta (WhatsApp/Instagram/Facebook)
/// якхела аст — мантиқи хосси провайдер нест.
/// </summary>
public static class WebhookSignature
{
    private const string Prefix = "sha256=";

    public static bool IsValid(string rawBody, string? signatureHeader, string appSecret)
    {
        if (string.IsNullOrEmpty(signatureHeader) || !signatureHeader.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        byte[] expectedBytes;
        try
        {
            expectedBytes = Convert.FromHexString(signatureHeader[Prefix.Length..]);
        }
        catch (FormatException)
        {
            return false;
        }

        var computedBytes = HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), Encoding.UTF8.GetBytes(rawBody));

        return CryptographicOperations.FixedTimeEquals(computedBytes, expectedBytes);
    }
}
