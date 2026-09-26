namespace Office.Api.Media;

/// <summary>Ffmpeg/ffprobe барои ArgumentList (на сатри фармон) — то номи файли муштарӣ command injection надиҳад.</summary>
public static class FfmpegArgumentBuilder
{
    // Files come from people (a мизоҷ's upload, a fan's message), so ffmpeg reads them only as
    // local files, and only with the demuxers of the formats we expect. Otherwise a file could
    // say it is audio and be a playlist or an "ffconcat" list that makes ffmpeg open other
    // files or addresses (checked 2026-09-26: ffmpeg 8.1 still follows an ffconcat list under a
    // .webm name; with these limits it refuses it, and every real format still converts).
    public const string AudioDemuxers = "matroska,ogg,mov,mp3,aac,wav,amr,flac";
    public const string ImageDemuxers = "image2,jpeg_pipe,png_pipe,webp_pipe,gif,gif_pipe,bmp_pipe,tiff_pipe,mov,matroska";

    private static string[] Input(string inputPath, string demuxers) =>
        ["-protocol_whitelist", "file", "-format_whitelist", demuxers, "-i", inputPath];

    public static IReadOnlyList<string> TranscodeToOggOpus(string inputPath, string outputPath) =>
    [
        "-y",
        .. Input(inputPath, AudioDemuxers),
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
        .. Input(inputPath, AudioDemuxers),
        "-c:a", "aac",
        "-b:a", "64k",
        "-ac", "1",
        "-vn",
        outputPath,
    ];

    public static IReadOnlyList<string> GenerateImageThumbnail(string inputPath, string outputPath, int maxDimension) =>
    [
        "-y",
        .. Input(inputPath, ImageDemuxers),
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
        .. Input(inputPath, ImageDemuxers),
        "-frames:v", "1",
        "-vf", "scale='min(iw,4096)':-2",
        "-q:v", "3",
        outputPath,
    ];

    public static IReadOnlyList<string> ProbeDurationSeconds(string inputPath) =>
    [
        "-v", "error",
        "-protocol_whitelist", "file",
        "-format_whitelist", AudioDemuxers,
        "-show_entries", "format=duration",
        "-of", "default=noprint_wrappers=1:nokey=1",
        inputPath,
    ];

    /// <summary>PCM хом (16-bit signed mono, 8kHz — барои пикҳои шакли мавҷ кофист, decode-и вазнинро кам мекунад).</summary>
    public static IReadOnlyList<string> ExtractRawPcm(string inputPath, string outputPath) =>
    [
        "-y",
        .. Input(inputPath, AudioDemuxers),
        "-ac", "1",
        "-ar", "8000",
        "-f", "s16le",
        outputPath,
    ];
}
