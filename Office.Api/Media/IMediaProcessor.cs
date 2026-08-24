namespace Office.Api.Media;

public interface IMediaProcessor
{
    Task TranscodeToOggOpusAsync(string inputPath, string outputPath, CancellationToken ct);

    /// <summary>
    /// Facebook/Instagram message_attachments намепазирад audio/ogg (Opus) — ниг. Meta docs
    /// (developers.facebook.com), формати иҷозатшуда: aac, m4a, wav, mp4. WhatsApp (боло) танҳо
    /// ogg/opus-ро ҳамчун voice note нишон медиҳад — ду роҳи ҷудогона лозим аст.
    /// </summary>
    Task TranscodeToAacAsync(string inputPath, string outputPath, CancellationToken ct);

    Task<int?> GetAudioDurationSecondsAsync(string inputPath, CancellationToken ct);

    Task GenerateImageThumbnailAsync(string inputPath, string outputPath, int maxDimension, CancellationToken ct);

    Task<IReadOnlyList<short>> GenerateWaveformPeaksAsync(string inputPath, int peakCount, CancellationToken ct);
}

public class MediaProcessingException(string message) : Exception(message);
