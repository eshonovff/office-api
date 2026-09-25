using System.Security.Cryptography;
using System.Text;

namespace Office.Api.Features.CustomerAuth;

public enum PasswordResetSendResult
{
    Sent,
    /// <summary>No such account, not verified, or switched off — nothing is sent, and the caller never learns which.</summary>
    NoAccount,
    CoolingDown,
    DailyCapReached,
}

/// <summary>
/// The rules of a "forgot password" link, kept pure so they are tested on their own. See
/// docs/phases/phase-14-customer-automations.md, "Барқарор кардани рамзи мизоҷ", for the threats.
/// </summary>
public static class PasswordResetRules
{
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan CapWindow = TimeSpan.FromHours(24);
    public const int MaxLinksPerWindow = 5;

    /// <summary>256 random bits, URL-safe — not a short code: there is nothing to guess.</summary>
    public static (string Token, string Hash) NewToken()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (token, Hash(token));
    }

    /// <summary>What the database keeps instead of the token (64 hex chars).</summary>
    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <param name="recentSends">When this account's links were created within <see cref="CapWindow"/>.</param>
    public static PasswordResetSendResult CanSend(IReadOnlyCollection<DateTimeOffset> recentSends, DateTimeOffset now)
    {
        if (recentSends.Any(sentAt => now - sentAt < Cooldown))
            return PasswordResetSendResult.CoolingDown;
        if (recentSends.Count(sentAt => now - sentAt < CapWindow) >= MaxLinksPerWindow)
            return PasswordResetSendResult.DailyCapReached;
        return PasswordResetSendResult.Sent;
    }
}
