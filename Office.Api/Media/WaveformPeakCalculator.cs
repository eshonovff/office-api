using System.Buffers.Binary;

namespace Office.Api.Media;

/// <summary>
/// PCM (16-bit signed mono) → пикҳои шакли мавҷ — pure, бе ffmpeg/файл. Ҳар baket
/// (қисми вақти баробар) ба амплитудаи максималии худ table мешавад, сипас ба 0-100
/// нисбат ба пики умумӣ нормализатсия мешавад (0-1 дар DTO, на дар захира — barои
/// фишурдагӣ smallint истифода мешавад).
/// </summary>
public static class WaveformPeakCalculator
{
    public const int DefaultPeakCount = 40;

    public static IReadOnlyList<short> Compute(IReadOnlyList<short> samples, int peakCount)
    {
        if (peakCount <= 0)
            return [];

        if (samples.Count == 0)
            return new short[peakCount];

        var bucketSize = Math.Max(1, (int)Math.Ceiling(samples.Count / (double)peakCount));
        var rawPeaks = new int[peakCount];

        for (var i = 0; i < peakCount; i++)
        {
            var start = i * bucketSize;
            if (start >= samples.Count)
                break;

            var end = Math.Min(start + bucketSize, samples.Count);
            var max = 0;
            for (var j = start; j < end; j++)
            {
                var abs = Math.Abs((int)samples[j]);
                if (abs > max)
                    max = abs;
            }

            rawPeaks[i] = max;
        }

        var overallMax = 0;
        foreach (var peak in rawPeaks)
        {
            if (peak > overallMax)
                overallMax = peak;
        }

        // Садои хомӯш ё қариб хомӯш — тақсим ба сифр намедиҳем, массиви ҳамвор (0) бармегардонем.
        if (overallMax == 0)
            return new short[peakCount];

        var normalized = new short[peakCount];
        for (var i = 0; i < peakCount; i++)
            normalized[i] = (short)Math.Round(rawPeaks[i] * 100.0 / overallMax);

        return normalized;
    }

    /// <summary>ffmpeg-ро мо ҳамеша "-f s16le" мехоҳем — бинобар ин LittleEndian ошкоро, на BitConverter-и вобаста ба host.</summary>
    public static short[] ToSamples(byte[] pcmBytes)
    {
        var sampleCount = pcmBytes.Length / 2;
        var samples = new short[sampleCount];
        for (var i = 0; i < sampleCount; i++)
            samples[i] = BinaryPrimitives.ReadInt16LittleEndian(pcmBytes.AsSpan(i * 2, 2));

        return samples;
    }
}
