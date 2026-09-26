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
    public void ConvertImageToJpeg_OneFrame_AtMost4096Wide_NameKeptWhole()
    {
        var args = FfmpegArgumentBuilder.ConvertImageToJpeg("a.webp; rm -rf / #", "out put.jpg");

        Assert.Single(args, a => a == "a.webp; rm -rf / #"); // one argument, never a shell string
        Assert.Contains("scale='min(iw,4096)':-2", args); // a big photo is made smaller, a small one never bigger
        Assert.Equal(["-frames:v", "1"], args.SkipWhile(a => a != "-frames:v").Take(2));
        Assert.Equal("out put.jpg", args[^1]);
    }

    [Fact]
    public void ProbeDurationSeconds_TargetsFormatDurationOnly()
    {
        var args = FfmpegArgumentBuilder.ProbeDurationSeconds("voice.ogg");

        Assert.Contains("format=duration", args);
        Assert.Contains("voice.ogg", args);
        Assert.Equal("voice.ogg", args[^1]);
    }

    public static TheoryData<string, string[], string> EveryInput => new()
    {
        { "ogg", [.. FfmpegArgumentBuilder.TranscodeToOggOpus("in.webm", "o.ogg")], FfmpegArgumentBuilder.AudioDemuxers },
        { "aac", [.. FfmpegArgumentBuilder.TranscodeToAac("in.webm", "o.m4a")], FfmpegArgumentBuilder.AudioDemuxers },
        { "pcm", [.. FfmpegArgumentBuilder.ExtractRawPcm("in.webm", "o.pcm")], FfmpegArgumentBuilder.AudioDemuxers },
        { "probe", [.. FfmpegArgumentBuilder.ProbeDurationSeconds("in.webm")], FfmpegArgumentBuilder.AudioDemuxers },
        { "jpeg", [.. FfmpegArgumentBuilder.ConvertImageToJpeg("in.webm", "o.jpg")], FfmpegArgumentBuilder.ImageDemuxers },
        { "thumb", [.. FfmpegArgumentBuilder.GenerateImageThumbnail("in.webm", "o.jpg", 128)], FfmpegArgumentBuilder.ImageDemuxers },
    };

    [Theory]
    [MemberData(nameof(EveryInput))]
    public void EveryInput_IsReadOnlyAsALocalFile_WithTheExpectedFormatsOnly(string _, string[] args, string demuxers)
    {
        var input = Array.IndexOf(args, "in.webm");
        Assert.True(input > 0);
        var before = args[..input];
        Assert.Equal("file", before[Array.IndexOf(before, "-protocol_whitelist") + 1]);
        Assert.Equal(demuxers, before[Array.IndexOf(before, "-format_whitelist") + 1]);
    }

    [Fact]
    public void TheAllowedFormats_NeverIncludeOnesThatOpenOtherFilesOrAddresses()
    {
        foreach (var list in new[] { FfmpegArgumentBuilder.AudioDemuxers, FfmpegArgumentBuilder.ImageDemuxers })
            Assert.Empty(list.Split(',').Intersect(["hls", "concat", "tty", "lavfi", "sdp", "rtp", "rtsp", "data", "image2pipe", "mpjpeg"]));
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
