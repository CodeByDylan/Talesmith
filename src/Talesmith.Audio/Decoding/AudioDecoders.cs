using System.Buffers;
using Talesmith.Assets;

namespace Talesmith.Audio.Decoding;

/// <summary>Opens the decoder for an audio asset by its extension.</summary>
public static class AudioDecoders
{
    /// <summary>The lower-case extensions that can be decoded, including the dot.</summary>
    public static IReadOnlyList<string> Extensions { get; } = [".wav", ".ogg"];

    /// <summary>Opens a decoder that owns <paramref name="stream"/>; non-seekable streams are copied into memory so they can rewind.</summary>
    /// <exception cref="AssetException">The format is unknown or the data is invalid.</exception>
    public static IAudioDecoder Open(Stream stream, string path)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(path);
        var extension = AssetPath.GetExtension(path);
        if (!Extensions.Contains(extension))
        {
            stream.Dispose();
            throw new AssetException($"The audio format '{extension}' of '{path}' is not supported.");
        }

        var seekable = stream.CanSeek ? stream : CopyToMemory(stream);
        try
        {
            return extension == ".wav" ? new WavDecoder(seekable) : new VorbisDecoder(seekable);
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
        {
            seekable.Dispose();
            throw new AssetException($"The audio file '{path}' is invalid: {ex.Message}", ex);
        }
    }

    /// <summary>Decodes everything that is left in a decoder.</summary>
    public static short[] ReadToEnd(IAudioDecoder decoder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        var samples = new ArrayBufferWriter<short>(1 << 16);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = decoder.Read(samples.GetSpan(1 << 16));
            if (read == 0)
                return samples.WrittenSpan.ToArray();
            samples.Advance(read);
        }
    }

    private static MemoryStream CopyToMemory(Stream stream)
    {
        var memory = new MemoryStream();
        using (stream)
            stream.CopyTo(memory);
        memory.Position = 0;
        return memory;
    }
}
