using Talesmith.Audio;
using Talesmith.Audio.Decoding;

namespace Talesmith.Editor.Assets.Previews;

/// <summary>The loudness outline of a sound: the lowest and highest sample of every block of frames, across channels, from -1 to 1.</summary>
public sealed class Waveform
{
    /// <summary>Frames per block of <see cref="Minimums"/> and <see cref="Maximums"/>.</summary>
    public const int FramesPerBlock = 128;

    private Waveform(float[] minimums, float[] maximums, int sampleRate, int channels, long frames)
    {
        Minimums = minimums;
        Maximums = maximums;
        SampleRate = sampleRate;
        Channels = channels;
        Frames = frames;
    }

    public IReadOnlyList<float> Minimums { get; }

    public IReadOnlyList<float> Maximums { get; }

    public int SampleRate { get; }

    public int Channels { get; }

    public long Frames { get; }

    public double Duration => SampleRate == 0 ? 0 : Frames / (double)SampleRate;

    /// <summary>Decodes a file and measures it; streams through the file, so long music needs little memory.</summary>
    public static Waveform Read(string file, CancellationToken cancellationToken = default)
    {
        using var decoder = AudioDecoders.Open(File.OpenRead(file), file);
        return Read(decoder, cancellationToken);
    }

    public static Waveform Read(IAudioDecoder decoder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        var channels = Math.Max(1, decoder.Channels);
        var buffer = new short[FramesPerBlock * channels * 64];
        var minimums = new List<float>();
        var maximums = new List<float>();
        long frames = 0;
        int count = 0;
        short low = 0, high = 0;
        int read;
        while ((read = decoder.Read(buffer)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var i = 0; i < read; i++)
            {
                var sample = buffer[i];
                if (sample < low)
                    low = sample;
                if (sample > high)
                    high = sample;
                if (++count == FramesPerBlock * channels)
                {
                    minimums.Add(low / 32768f);
                    maximums.Add(high / 32767f);
                    low = high = 0;
                    count = 0;
                }
            }

            frames += read / channels;
        }

        if (count > 0)
        {
            minimums.Add(low / 32768f);
            maximums.Add(high / 32767f);
        }

        return new Waveform([.. minimums], [.. maximums], decoder.SampleRate, channels, frames);
    }

    /// <summary>The lowest and highest value between two fractions of the sound's length.</summary>
    public (float Min, float Max) Range(double from, double to)
    {
        var count = Minimums.Count;
        if (count == 0)
            return (0, 0);
        var first = Math.Clamp((int)(from * count), 0, count - 1);
        var last = Math.Clamp((int)Math.Ceiling(to * count), first + 1, count);
        float min = 0, max = 0;
        for (var i = first; i < last; i++)
        {
            min = Math.Min(min, Minimums[i]);
            max = Math.Max(max, Maximums[i]);
        }

        return (min, max);
    }
}
