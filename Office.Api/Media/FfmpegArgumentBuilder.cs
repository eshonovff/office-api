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

    public static IReadOnlyList<string> GenerateImageThumbnail(string inputPath, string outputPath, int maxDimension) =>
    [
        "-y",
        "-i", inputPath,
        "-vf", $"scale={maxDimension}:{maxDimension}:force_original_aspect_ratio=decrease",
        "-frames:v", "1",
        outputPath,
    ];

    public static IReadOnlyList<string> ProbeDurationSeconds(string inputPath) =>
    [
        "-v", "error",
        "-show_entries", "format=duration",
        "-of", "default=noprint_wrappers=1:nokey=1",
        inputPath,
    ];
}
