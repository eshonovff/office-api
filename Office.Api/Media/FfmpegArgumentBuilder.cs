namespace Office.Api.Media;

/// <summary>Ffmpeg/ffprobe барои ArgumentList (на сатри фармон) — то номи файли муштарӣ command injection надиҳад.</summary>
public static class FfmpegArgumentBuilder
{
    public static IReadOnlyList<string> TranscodeToOggOpus(string inputPath, string outputPath) =>
    [
        "-y",
        "-i", inputPath,
        "-c:a", "libopus",
        "-b:a", "32k",
        "-ac", "1",
        "-vn",
        outputPath,
    ];

    /// <summary>Facebook/Instagram audio attachment (aac/m4a/wav/mp4 — на ogg/opus, ниг. IMediaProcessor.TranscodeToAacAsync).</summary>
    public static IReadOnlyList<string> TranscodeToAac(string inputPath, string outputPath) =>
    [
        "-y",
        "-i", inputPath,
        "-c:a", "aac",
        "-b:a", "64k",
        "-ac", "1",
        "-vn",
        outputPath,
    ];

    public static IReadOnlyList<string> GenerateImageThumbnail(string inputPath, string outputPath, int maxDimension) =>
    [
        "-y",
        "-i", inputPath,
        "-vf", $"scale={maxDimension}:{maxDimension}:force_original_aspect_ratio=decrease",
        "-frames:v", "1",
        outputPath,
    ];

    /// <summary>
    /// Any image ffmpeg can read (WEBP, HEIC, BMP…) as a JPEG — the first frame, at most 4096 px
    /// wide (a phone photo stays sharp and well under Instagram's 8 MB).
    /// </summary>
    public static IReadOnlyList<string> ConvertImageToJpeg(string inputPath, string outputPath) =>
    [
        "-y",
        "-i", inputPath,
        "-frames:v", "1",
        "-vf", "scale='min(iw,4096)':-2",
        "-q:v", "3",
        outputPath,
    ];

    public static IReadOnlyList<string> ProbeDurationSeconds(string inputPath) =>
    [
        "-v", "error",
        "-show_entries", "format=duration",
        "-of", "default=noprint_wrappers=1:nokey=1",
        inputPath,
    ];

    /// <summary>PCM хом (16-bit signed mono, 8kHz — барои пикҳои шакли мавҷ кофист, decode-и вазнинро кам мекунад).</summary>
    public static IReadOnlyList<string> ExtractRawPcm(string inputPath, string outputPath) =>
    [
        "-y",
        "-i", inputPath,
        "-ac", "1",
        "-ar", "8000",
        "-f", "s16le",
        outputPath,
    ];
}
