using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Office.Api.Media;

namespace Office.Api.Tests.Media;

/// <summary>
/// The real ffmpeg with our arguments: every format people really send still converts, and a file
/// that only pretends to be audio (an "ffconcat" list pointing at another file next to it) is
/// refused. Without ffmpeg on the machine there is nothing to run, and the tests end early.
/// </summary>
public sealed class FfmpegLimitsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ffmpeg-limits-").FullName;
    private readonly FfmpegMediaProcessor _processor = new(NullLogger<FfmpegMediaProcessor>.Instance);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static bool HasFfmpeg()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("ffmpeg", "-version") { RedirectStandardOutput = true, RedirectStandardError = true });
            process!.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>A sample made by ffmpeg itself (its generators, not a file anyone sent).</summary>
    private string Make(string name, params string[] args)
    {
        var path = Path.Combine(_dir, name);
        var startInfo = new ProcessStartInfo("ffmpeg") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in (string[])["-hide_banner", "-loglevel", "error", "-y", .. args, path])
            startInfo.ArgumentList.Add(arg);
        using var process = Process.Start(startInfo)!;
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"could not make {name}");
        return path;
    }

    private string Tone(string name, params string[] codec) => Make(name, ["-f", "lavfi", "-i", "sine=frequency=440:duration=2", .. codec]);

    private string Picture(string name) => Make(name, "-f", "lavfi", "-i", "testsrc=size=160x120", "-frames:v", "1");

    [Fact]
    public async Task EveryRecordingAndAudioFormat_StillConverts_AndIsMeasured()
    {
        if (!HasFfmpeg()) return;
        string[] samples =
        [
            Tone("chrome.webm", "-c:a", "libopus"),
            Tone("firefox.ogg", "-c:a", "libopus"),
            Tone("safari.mp4", "-c:a", "aac", "-movflags", "frag_keyframe+empty_moov+default_base_moof"),
            Tone("file.m4a", "-c:a", "aac"),
            Tone("file.wav"),
            Tone("file.mp3", "-c:a", "libmp3lame"),
        ];

        foreach (var sample in samples)
        {
            // Stored under our own name, as the flow upload does (".src") — the content decides.
            var src = Path.ChangeExtension(sample, ".src");
            File.Copy(sample, src, overwrite: true);
            foreach (var input in new[] { sample, src })
            {
                var output = Path.Combine(_dir, $"{Guid.NewGuid()}.m4a");
                await _processor.TranscodeToAacAsync(input, output, CancellationToken.None);
                Assert.True(new FileInfo(output).Length > 0, input);
                Assert.Equal(2, await _processor.GetAudioDurationSecondsAsync(input, CancellationToken.None));
                Assert.NotEmpty(await _processor.GenerateWaveformPeaksAsync(input, 20, CancellationToken.None));
            }
        }
    }

    [Fact]
    public async Task EveryPictureFormat_StillConverts()
    {
        if (!HasFfmpeg()) return;
        var webp = Path.Combine(_dir, "tiny.webp");
        await File.WriteAllBytesAsync(webp, Convert.FromBase64String("UklGRhoAAABXRUJQVlA4TA0AAAAvAAAAEAcQERGIiP4HAA=="));

        foreach (var picture in new[] { Picture("p.png"), Picture("p.jpg"), Picture("p.gif"), Picture("p.bmp"), webp })
        {
            var src = Path.ChangeExtension(picture, ".src");
            File.Copy(picture, src, overwrite: true);
            foreach (var input in new[] { picture, src })
            {
                var jpeg = Path.Combine(_dir, $"{Guid.NewGuid()}.jpg");
                await _processor.ConvertImageToJpegAsync(input, jpeg, CancellationToken.None);
                Assert.True(new FileInfo(jpeg).Length > 0, input);
                var thumb = Path.Combine(_dir, $"{Guid.NewGuid()}.jpg");
                await _processor.GenerateImageThumbnailAsync(input, thumb, 64, CancellationToken.None);
                Assert.True(new FileInfo(thumb).Length > 0, input);
            }
        }
    }

    [Fact]
    public async Task AFileThatOnlyPretendsToBeAudio_CannotMakeFfmpegReadAnotherFile()
    {
        if (!HasFfmpeg()) return;
        Tone("someone-elses.webm", "-c:a", "libopus");
        var crafted = Path.Combine(_dir, "voice.webm");
        await File.WriteAllTextAsync(crafted, "ffconcat version 1.0\nfile someone-elses.webm\n");

        await Assert.ThrowsAsync<MediaProcessingException>(
            () => _processor.TranscodeToAacAsync(crafted, Path.Combine(_dir, "out.m4a"), CancellationToken.None));
        await Assert.ThrowsAsync<MediaProcessingException>(
            () => _processor.ConvertImageToJpegAsync(crafted, Path.Combine(_dir, "out.jpg"), CancellationToken.None));
        await Assert.ThrowsAsync<MediaProcessingException>(
            () => _processor.GetAudioDurationSecondsAsync(crafted, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(_dir, "out.m4a")));
    }
}
