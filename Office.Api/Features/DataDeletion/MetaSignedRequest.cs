using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Office.Api.Features.DataDeletion;

public sealed record MetaSignedRequestPayload(string UserId, DateTimeOffset IssuedAt);

/// <summary>
/// Meta's <c>signed_request</c> ("{base64url HMAC-SHA256 signature}.{base64url JSON payload}",
/// signed with the app secret) — the only proof that a data-deletion call really comes from Meta.
/// The endpoint receiving it is anonymous and public, and what it triggers is irreversible, so
/// anything not provably Meta's, fresh, and well-formed is refused.
/// </summary>
public static class MetaSignedRequest
{
    /// <summary>Older than this is treated as a replay (see DataDeletionEndpoints for the rest).</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    /// <summary>Tolerated clock difference for an issued_at slightly in our future.</summary>
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    public static MetaSignedRequestPayload? TryVerify(string? signedRequest, string appSecret, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(signedRequest) || string.IsNullOrEmpty(appSecret))
            return null;

        var parts = signedRequest.Split('.');
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
            return null;

        byte[] signature;
        byte[] payloadBytes;
        try
        {
            signature = Base64UrlDecode(parts[0]);
            payloadBytes = Base64UrlDecode(parts[1]);
        }
        catch (FormatException)
        {
            return null;
        }

        // Signature first — the payload of an unverified request is never even parsed.
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), Encoding.UTF8.GetBytes(parts[1]));
        if (!CryptographicOperations.FixedTimeEquals(expected, signature))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(payloadBytes);
            var root = doc.RootElement;

            if (!root.TryGetProperty("algorithm", out var algorithm) ||
                !string.Equals(algorithm.GetString(), "HMAC-SHA256", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var userId = root.TryGetProperty("user_id", out var userIdEl)
                ? userIdEl.ValueKind switch
                {
                    JsonValueKind.String => userIdEl.GetString(),
                    JsonValueKind.Number => userIdEl.GetRawText(),
                    _ => null,
                }
                : null;
            if (string.IsNullOrWhiteSpace(userId))
                return null;

            if (!root.TryGetProperty("issued_at", out var issuedAtEl) || !issuedAtEl.TryGetInt64(out var issuedAtUnix))
                return null;

            var issuedAt = DateTimeOffset.FromUnixTimeSeconds(issuedAtUnix);
            if (issuedAt < now - MaxAge || issuedAt > now + MaxClockSkew)
                return null;

            return new MetaSignedRequestPayload(userId, issuedAt);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
}
