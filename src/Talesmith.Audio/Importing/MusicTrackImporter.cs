using Microsoft.Extensions.Logging;
using Talesmith.Assets;
using Talesmith.Audio.Decoding;

namespace Talesmith.Audio.Importing;

/// <summary>Imports Ogg Vorbis and WAV files as <see cref="MusicTrack"/>s that are decoded while they play.</summary>
/// <remarks>
/// With <see cref="AudioLoadType.Preload"/> the file is read into memory once and every decoder reads from there; otherwise importing
/// reads only the header to validate the file, and every decoder the track opens reads the file again.
/// </remarks>
public sealed partial class MusicTrackImporter : AssetImporter<MusicTrack, AudioImportSettings>
{
    public override IReadOnlyList<string> Extensions => AudioDecoders.Extensions;

    public override string Id => SoundClipImporter.ImporterId;

    public override async Task<MusicTrack> ImportAsync(AssetImportContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var settings = context.GetSettings<AudioImportSettings>();
        var source = context.Source;
        var path = context.Path;
        Func<Stream> open = () => source.OpenRead(path);
        if (settings.LoadType == AudioLoadType.Preload)
        {
            using var buffer = new MemoryStream();
            var stream = source.OpenRead(path);
            await using (stream.ConfigureAwait(false))
                await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            var bytes = buffer.ToArray();
            open = () => new MemoryStream(bytes, writable: false);
        }

        int sampleRate;
        long? frames;
        using (var decoder = AudioDecoders.Open(open(), path))
        {
            sampleRate = decoder.SampleRate;
            frames = decoder is WavDecoder wav ? wav.Frames : null;
        }

        var (loopStart, loopEnd) = LoopPoints.ToFrames(settings, sampleRate, frames, out var warning);
        if (warning is not null)
            LogLoopWarning(context.Logger, path, warning);

        return new MusicTrack(path, () => AudioDecoders.Open(open(), path))
        {
            Loop = settings.Loop,
            LoopStart = loopStart,
            LoopEnd = loopEnd,
            Volume = Math.Clamp(settings.Volume, 0, 1)
        };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Path}: {Warning}")]
    private static partial void LogLoopWarning(ILogger logger, string path, string warning);
}
