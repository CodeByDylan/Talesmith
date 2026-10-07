using Microsoft.Extensions.Logging;
using Talesmith.Assets;
using Talesmith.Audio.Decoding;

namespace Talesmith.Audio.Importing;

/// <summary>Imports WAV and Ogg Vorbis files as fully decoded <see cref="SoundClip"/>s, applying their <see cref="AudioImportSettings"/>.</summary>
public sealed partial class SoundClipImporter : AssetImporter<SoundClip, AudioImportSettings>
{
    public const string ImporterId = "audio";

    public override IReadOnlyList<string> Extensions => AudioDecoders.Extensions;

    public override string Id => ImporterId;

    public override Task<SoundClip> ImportAsync(AssetImportContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var settings = context.GetSettings<AudioImportSettings>();
        using var decoder = AudioDecoders.Open(context.OpenRead(), context.Path);
        var samples = AudioDecoders.ReadToEnd(decoder, cancellationToken);
        var frames = samples.Length / decoder.Channels;
        var (loopStart, loopEnd) = LoopPoints.ToFrames(settings, decoder.SampleRate, frames, out var warning);
        if (warning is not null)
            LogLoopWarning(context.Logger, context.Path, warning);

        return Task.FromResult(new SoundClip(context.Path, decoder.SampleRate, decoder.Channels, samples)
        {
            Loop = settings.Loop,
            LoopStart = (int)loopStart,
            LoopEnd = (int)loopEnd,
            Bus = settings.Bus,
            Volume = Math.Clamp(settings.Volume, 0, 1)
        });
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Path}: {Warning}")]
    private static partial void LogLoopWarning(ILogger logger, string path, string warning);
}

/// <summary>Converts loop points in seconds to sample frames.</summary>
internal static class LoopPoints
{
    /// <param name="frames">The length of the audio in frames, or null when it is not known.</param>
    public static (long Start, long End) ToFrames(AudioImportSettings settings, int sampleRate, long? frames, out string? warning)
    {
        warning = null;
        var start = (long)Math.Round(Math.Max(settings.LoopStart, 0) * sampleRate);
        var end = settings.LoopEnd is { } seconds ? (long)Math.Round(seconds * sampleRate) : 0;
        if (frames is { } length)
        {
            start = Math.Min(start, length);
            end = end >= length ? 0 : end;
        }

        if (end > 0 && end <= start)
        {
            warning = $"The loop end ({settings.LoopEnd:0.###} s) is not after the loop start ({settings.LoopStart:0.###} s), so the whole sound loops.";
            return (0, 0);
        }

        return (start, end);
    }
}
