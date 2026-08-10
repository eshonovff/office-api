using Office.Api.Media;

namespace Office.Api.Tests.Media;

public class FfmpegArgumentBuilderTests
{
    [Fact]
    public void TranscodeToOggOpus_PassesInputAndOutputAsSeparateArguments()
    {
        var args = FfmpegArgumentBuilder.TranscodeToOggOpus("in put.webm", "out put.ogg");

        Assert.Contains("in put.webm", args);
        Assert.Contains("out put.ogg", args);
        Assert.Contains("libopus", args);
        Assert.Equal("out put.ogg", args[^1]);
    }

    [Fact]
    public void TranscodeToOggOpus_KeepsAShellMetacharacterFilenameAsOneUntouchedElement()
    {
        const string maliciousName = "a.webm; rm -rf / #";

        var args = FfmpegArgumentBuilder.TranscodeToOggOpus(maliciousName, "b.ogg");

        Assert.Single(args, a => a == maliciousName);
        Assert.DoesNotContain(args, a => a.Contains("rm -rf") && a != maliciousName);
    }

    [Fact]
    public void GenerateImageThumbnail_UsesMaxDimensionInScaleFilter()
    {
        var args = FfmpegArgumentBuilder.GenerateImageThumbnail("photo.jpg", "thumb.jpg", 320);

        Assert.Contains("scale=320:320:force_original_aspect_ratio=decrease", args);
        Assert.Contains("photo.jpg", args);
        Assert.Contains("thumb.jpg", args);
    }

    [Fact]
    public void ProbeDurationSeconds_TargetsFormatDurationOnly()
    {
        var args = FfmpegArgumentBuilder.ProbeDurationSeconds("voice.ogg");

        Assert.Contains("format=duration", args);
        Assert.Contains("voice.ogg", args);
        Assert.Equal("voice.ogg", args[^1]);
    }

    [Fact]
    public void AllBuilders_KeepEachArgumentAsASeparateArrayElement()
    {
        IReadOnlyList<string>[] allArgLists =
        [
            FfmpegArgumentBuilder.TranscodeToOggOpus("x.webm", "y.ogg"),
            FfmpegArgumentBuilder.GenerateImageThumbnail("x.jpg", "y.jpg", 320),
            FfmpegArgumentBuilder.ProbeDurationSeconds("x.ogg"),
        ];

        foreach (var args in allArgLists)
            Assert.All(args, a => Assert.False(string.IsNullOrEmpty(a)));
    }
}
