namespace Office.Api.Features.Subscriptions;

/// <summary>
/// Whether a receipt's first bytes are really what its extension claims. The extension alone is
/// just a name: an HTML or SVG page renamed "chek.png" would pass it, and a moderator opens every
/// receipt. Only the four formats a bank-app screenshot or statement can be are accepted.
/// </summary>
public static class ReceiptFileSignature
{
    /// <summary>Enough for the longest check below (WEBP: "RIFF" + size + "WEBP").</summary>
    public const int HeaderLength = 12;

    public static bool Matches(string extension, ReadOnlySpan<byte> header) => extension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => header.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]),
        ".png" => header.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
        ".webp" => header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8),
        ".pdf" => header.StartsWith("%PDF-"u8),
        _ => false,
    };
}
