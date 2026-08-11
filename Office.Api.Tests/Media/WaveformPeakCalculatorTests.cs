using Office.Api.Media;

namespace Office.Api.Tests.Media;

public class WaveformPeakCalculatorTests
{
    [Fact]
    public void Compute_SilentAudio_ReturnsFlatZeroArrayNotNaNOrException()
    {
        var samples = new short[8000]; // ҳама сифр — тамоман хомӯш

        var peaks = WaveformPeakCalculator.Compute(samples, 40);

        Assert.Equal(40, peaks.Count);
        Assert.All(peaks, p => Assert.Equal(0, p));
    }

    [Fact]
    public void Compute_EmptySamples_ReturnsFlatZeroArrayOfRequestedLength()
    {
        var peaks = WaveformPeakCalculator.Compute([], 40);

        Assert.Equal(40, peaks.Count);
        Assert.All(peaks, p => Assert.Equal(0, p));
    }

    [Fact]
    public void Compute_ZeroOrNegativePeakCount_ReturnsEmpty()
    {
        Assert.Empty(WaveformPeakCalculator.Compute([1, 2, 3], 0));
        Assert.Empty(WaveformPeakCalculator.Compute([1, 2, 3], -5));
    }

    [Fact]
    public void Compute_ReturnsExactlyTheRequestedPeakCount()
    {
        var samples = Enumerable.Range(0, 16000).Select(i => (short)(i % 1000)).ToArray();

        Assert.Equal(40, WaveformPeakCalculator.Compute(samples, 40).Count);
        Assert.Equal(32, WaveformPeakCalculator.Compute(samples, 32).Count);
    }

    [Fact]
    public void Compute_LoudestBucketNormalizesToExactly100()
    {
        var samples = new short[100];
        samples[50] = 12345; // як баket аз дигарҳо баландтар

        var peaks = WaveformPeakCalculator.Compute(samples, 10);

        Assert.Equal(100, peaks.Max());
    }

    [Fact]
    public void Compute_AllValuesWithinZeroToHundred()
    {
        var random = new Random(42);
        var samples = new short[48000];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = (short)random.Next(short.MinValue, short.MaxValue);

        var peaks = WaveformPeakCalculator.Compute(samples, 40);

        Assert.All(peaks, p => Assert.InRange(p, (short)0, (short)100));
    }

    [Fact]
    public void Compute_FewerSamplesThanPeaks_TrailingPeaksAreZeroNotGarbage()
    {
        short[] samples = [1000, 2000, 3000]; // хеле кӯтоҳ — аз 40 baket хеле камтар, ҳар sample baket-и худро мегирад

        var peaks = WaveformPeakCalculator.Compute(samples, 40);

        Assert.Equal(40, peaks.Count);
        Assert.Equal(100, peaks[2]); // sample-и баландтарин (3000) дар baket-и сеюм
        Assert.All(peaks.Skip(3), p => Assert.Equal(0, p));
    }

    [Fact]
    public void Compute_NegativeAmplitudesUseAbsoluteValue()
    {
        short[] samples = [short.MinValue, 0, 0, 0];

        var peaks = WaveformPeakCalculator.Compute(samples, 4);

        Assert.Equal(100, peaks[0]);
    }

    [Fact]
    public void ToSamples_ParsesLittleEndianInt16Correctly()
    {
        // 0x0100 = 256 (LE: байти пасттар аввал), 0xFFFF = -1
        byte[] pcm = [0x00, 0x01, 0xFF, 0xFF];

        var samples = WaveformPeakCalculator.ToSamples(pcm);

        Assert.Equal([256, -1], samples);
    }

    [Fact]
    public void ToSamples_OddByteCount_IgnoresTrailingByte()
    {
        byte[] pcm = [0x00, 0x01, 0x02]; // 3 байт — 1 sample пурра, 1 байти нопурра

        var samples = WaveformPeakCalculator.ToSamples(pcm);

        Assert.Single(samples);
    }
}
