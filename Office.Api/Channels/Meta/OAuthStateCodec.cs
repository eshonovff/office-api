using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Office.Api.Channels.Meta;

/// <summary>Who started an OAuth connect: a staff user (company channel) or a мизоҷ (their own).</summary>
public enum OAuthOwnerKind
{
    // Staff = 0 on purpose: a state signed before this field existed decodes as Staff, which is
    // exactly who could start OAuth back then.
    Staff = 0,
    Customer = 1,
}

/// <param name="UserId">The owner's id — a staff User.Id or a Customer.Id, per <paramref name="OwnerKind"/>.</param>
public sealed record OAuthStatePayload(
    string Provider, Guid UserId, string Nonce, long ExpiresAtUnix, OAuthOwnerKind OwnerKind = OAuthOwnerKind.Staff);

/// <summary>
/// State-и OAuth-и Meta: имзошуда (HMAC-SHA256, алгуи <see cref="WebhookSignature"/>), кӯтоҳмуддат
/// ва ба корбари оғозкунанда бастashuда (UserId дар payload) — на танҳо CSRF token-и оддӣ.
/// Дуюм лои ҳимоя (якмаротибагӣ) дар <see cref="OAuthNonceTracker"/> аст: ин синф танҳо имзо
/// ва мӯҳлатро месанҷад, pure ва бе DB.
/// </summary>
public static class OAuthStateCodec
{
    private const string Domain = "Office.Api.Channels.OAuthState.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Encode(
        string provider,
        Guid userId,
        string nonce,
        DateTimeOffset expiresAt,
        string signingKey,
        OAuthOwnerKind ownerKind = OAuthOwnerKind.Staff)
    {
        var payload = new OAuthStatePayload(provider, userId, nonce, expiresAt.ToUnixTimeSeconds(), ownerKind);
        var payloadB64 = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions));
        var signature = Convert.ToHexString(ComputeSignature(payloadB64, signingKey));
        return $"{payloadB64}.{signature}";
    }

    public static bool TryDecode(string token, string signingKey, DateTimeOffset now, out OAuthStatePayload? payload)
    {
        payload = null;

        var separatorIndex = token.IndexOf('.');
        if (separatorIndex <= 0 || separatorIndex == token.Length - 1)
            return false;

        var payloadB64 = token[..separatorIndex];
        var signatureHex = token[(separatorIndex + 1)..];

        byte[] signatureBytes;
        try
        {
            signatureBytes = Convert.FromHexString(signatureHex);
        }
        catch (FormatException)
        {
            return false;
        }

        var expectedSignature = ComputeSignature(payloadB64, signingKey);
        if (!CryptographicOperations.FixedTimeEquals(expectedSignature, signatureBytes))
            return false;

        byte[] payloadBytes;
        try
        {
            payloadBytes = Base64UrlDecode(payloadB64);
        }
        catch (FormatException)
        {
            return false;
        }

        OAuthStatePayload? decoded;
        try
        {
            decoded = JsonSerializer.Deserialize<OAuthStatePayload>(payloadBytes, JsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        if (decoded is null || now.ToUnixTimeSeconds() > decoded.ExpiresAtUnix)
            return false;

        payload = decoded;
        return true;
    }

    private static byte[] ComputeSignature(string payloadB64, string signingKey) =>
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(signingKey), Encoding.UTF8.GetBytes($"{Domain}|{payloadB64}"));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
}
