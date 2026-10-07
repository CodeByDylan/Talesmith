using System.Buffers.Binary;
using Microsoft.Extensions.Logging.Abstractions;
using Talesmith.Assets;
using Talesmith.Audio.Decoding;
using Talesmith.Audio.Importing;

namespace Talesmith.Audio.Tests;

public sealed class AudioImportTests : IDisposable
{
    private const int SampleRate = 1000;

    private readonly string _folder = Directory.CreateTempSubdirectory("talesmith-audio-tests").FullName;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task ClipsCarryTheirImportSettings()
    {
        WriteWav("jump.wav", frames: 2000);
        var settings = new AudioImportSettings { Loop = true, LoopStart = 0.5, LoopEnd = 1.5, Bus = AudioBus.Interface, Volume = 0.25f };
        using var manager = Manager(("jump.wav", settings));

        var clip = await manager.LoadAsync<SoundClip>("jump.wav", Token);

        Assert.True(clip.Loop);
        Assert.Equal(500, clip.LoopStart);
        Assert.Equal(1500, clip.LoopEnd);
        Assert.True(clip.HasLoopRegion);
        Assert.Equal(AudioBus.Interface, clip.Bus);
        Assert.Equal(0.25f, clip.Volume);
        Assert.Equal(new SoundOptions(Loop: true, Bus: AudioBus.Interface), SoundOptions.For(clip));
    }

    [Fact]
    public async Task InvalidLoopRegionsLoopTheWholeSound()
    {
        WriteWav("hum.wav", frames: 2000);
        using var manager = Manager(("hum.wav", new AudioImportSettings { LoopStart = 1.5, LoopEnd = 0.5 }));

        var clip = await manager.LoadAsync<SoundClip>("hum.wav", Token);

        Assert.False(clip.HasLoopRegion);
    }

    [Fact]
    public async Task PreloadedMusicPlaysWithoutTheFile()
    {
        WriteWav("theme.wav", frames: 300);
        using var manager = Manager(("theme.wav", new AudioImportSettings { LoadType = AudioLoadType.Preload, LoopStart = 0.1 }));

        var track = await manager.LoadAsync<MusicTrack>("theme.wav", Token);
        File.Delete(Path.Combine(_folder, "theme.wav"));

        using var decoder = track.OpenDecoder();
        Assert.Equal(300, AudioDecoders.ReadToEnd(decoder, Token).Length);
        Assert.Equal(100, track.LoopStart);
    }

    [Fact]
    public void LoopRegionsPlayTheIntroOnceThenTheLoop()
    {
        using var decoder = new LoopRegionDecoder(new CountingDecoder(10), loopStart: 3, loopEnd: 7);

        Assert.Equal([0, 1, 2, 3, 4, 5, 6], Read(decoder));
        decoder.Rewind();
        Assert.Equal([3, 4, 5, 6], Read(decoder));
    }

    [Fact]
    public void SeekableDecodersJumpToTheLoopStart()
    {
        var inner = new SeekableCountingDecoder(10);
        using var decoder = new LoopRegionDecoder(inner, loopStart: 4, loopEnd: 0);

        Assert.Equal(10, Read(decoder).Length);
        decoder.Rewind();

        Assert.Equal([4, 5, 6, 7, 8, 9], Read(decoder));
        Assert.Equal(0, inner.Rewinds);
    }

    [Fact]
    public void WavDecodersSeekToFrames()
    {
        var bytes = Wav(frames: 10);
        using var decoder = new WavDecoder(new MemoryStream(bytes));

        decoder.Seek(6);

        Assert.Equal(10, decoder.Frames);
        Assert.Equal([6, 7, 8, 9], Read(decoder));
    }

    private static short[] Read(IAudioDecoder decoder) => AudioDecoders.ReadToEnd(decoder, Token);

    private AssetManager Manager(params (string Path, AudioImportSettings Settings)[] assets)
    {
        var catalog = new AssetCatalog();
        foreach (var (path, settings) in assets)
            catalog.Set(path, new AssetMeta(AssetGuid.NewGuid()) { Importer = SoundClipImporter.ImporterId }.WithSettings(settings));
        return new AssetManager(new FileSystemAssetSource(_folder), [new SoundClipImporter(), new MusicTrackImporter()], NullLogger<AssetManager>.Instance, catalog);
    }

    private void WriteWav(string name, int frames) => File.WriteAllBytes(Path.Combine(_folder, name), Wav(frames));

    /// <summary>A mono 16-bit WAV whose samples count up from 0.</summary>
    private static byte[] Wav(int frames)
    {
        var data = frames * 2;
        var bytes = new byte[44 + data];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 36 + data);
        "WAVEfmt "u8.CopyTo(bytes.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28), SampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(32), 2);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(34), 16);
        "data"u8.CopyTo(bytes.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40), data);
        for (var i = 0; i < frames; i++)
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(44 + i * 2), (short)i);
        return bytes;
    }

    private class CountingDecoder(int frames) : IAudioDecoder
    {
        public int Rewinds { get; private set; }

        public int SampleRate => AudioImportTests.SampleRate;

        public int Channels => 1;

        protected long Position { get; set; }

        public int Read(Span<short> buffer)
        {
            var count = (int)Math.Min(buffer.Length, frames - Position);
            for (var i = 0; i < count; i++)
                buffer[i] = (short)Position++;
            return count;
        }

        public void Rewind()
        {
            Rewinds++;
            Position = 0;
        }

        public void Dispose()
        {
        }
    }

    private sealed class SeekableCountingDecoder(int frames) : CountingDecoder(frames), ISeekableAudioDecoder
    {
        public void Seek(long frame) => Position = frame;
    }
}
