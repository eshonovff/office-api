namespace Office.Api.Media;

public interface IMediaProcessor
{
    Task TranscodeToOggOpusAsync(string inputPath, string outputPath, CancellationToken ct);

    Task<int?> GetAudioDurationSecondsAsync(string inputPath, CancellationToken ct);

    Task GenerateImageThumbnailAsync(string inputPath, string outputPath, int maxDimension, CancellationToken ct);
}

public class MediaProcessingException(string message) : Exception(message);
