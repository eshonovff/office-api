using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Office.Api.Media;

/// <summary>
/// Ҳар иҷрои ffmpeg/ffprobe: танҳо ArgumentList (на сатри фармон),
/// timeout-и ҳатмӣ бо куштани process дар хатогӣ/анҷоми вақт.
/// </summary>
public class FfmpegMediaProcessor(ILogger<FfmpegMediaProcessor> logger) : IMediaProcessor
{
    private static readonly TimeSpan ExecutionTimeout = TimeSpan.FromSeconds(30);

    public Task TranscodeToOggOpusAsync(string inputPath, string outputPath, CancellationToken ct) =>
        RunAsync("ffmpeg", FfmpegArgumentBuilder.TranscodeToOggOpus(inputPath, outputPath), ct);

    public Task GenerateImageThumbnailAsync(string inputPath, string outputPath, int maxDimension, CancellationToken ct) =>
        RunAsync("ffmpeg", FfmpegArgumentBuilder.GenerateImageThumbnail(inputPath, outputPath, maxDimension), ct);

    public async Task<int?> GetAudioDurationSecondsAsync(string inputPath, CancellationToken ct)
    {
        var output = await RunAsync("ffprobe", FfmpegArgumentBuilder.ProbeDurationSeconds(inputPath), ct);
        return double.TryParse(output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? (int)Math.Round(seconds)
            : null;
    }

    private async Task<string> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(ExecutionTimeout);

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            KillIfRunning(process);
            var reason = ct.IsCancellationRequested ? "cancelled" : $"timed out after {ExecutionTimeout.TotalSeconds}s";
            throw new MediaProcessingException($"{fileName} {reason}");
        }
        catch
        {
            KillIfRunning(process);
            throw;
        }

        if (process.ExitCode != 0)
        {
            logger.LogWarning("{FileName} exited with code {ExitCode}: {Stderr}", fileName, process.ExitCode, stderr.ToString());
            throw new MediaProcessingException($"{fileName} exited with code {process.ExitCode}: {stderr}");
        }

        return stdout.ToString();
    }

    private static void KillIfRunning(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Process хатми байни санҷиш ва Kill — бехатар нодида гирифта мешавад.
        }
    }
}
