namespace Office.Api.Media;

/// <summary>
/// What a browser records a voice note in — the only types a voice note is taken as, by staff and
/// мизоҷ alike: Chrome and Edge WebM/Opus, Firefox Ogg/Opus, Safari (iPhone too) AAC in MP4. Each
/// is stored with an extension MediaSendJob's transcode never writes (.m4a, .ogg), so every
/// recording goes through ffmpeg exactly once: checked to be audio, made the channel's format, measured.
/// </summary>
public static class VoiceNoteRecording
{
    public static readonly IReadOnlyDictionary<string, string> Types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["audio/webm"] = ".webm",
        ["audio/ogg"] = ".opus",
        ["audio/mp4"] = ".mp4",
    };

    /// <summary>"audio/webm;codecs=opus" → "audio/webm".</summary>
    public static string BaseType(string? contentType) => (contentType ?? "").Split(';')[0].Trim().ToLowerInvariant();

    /// <returns>The extension to store the recording with; null — not a recording.</returns>
    public static string? ExtensionFor(string baseType) => Types.TryGetValue(baseType, out var extension) ? extension : null;
}
