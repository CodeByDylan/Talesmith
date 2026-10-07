using Silk.NET.OpenAL;

namespace Talesmith.Audio.OpenAL;

/// <summary>Uploads clips to OpenAL buffers on first use and keeps the most recently used ones within a memory budget.</summary>
/// <remarks>
/// Not thread-safe; the audio service calls it under its lock. Buffers in use are never evicted, so the budget can be exceeded
/// temporarily. Clips with a loop region get OpenAL loop points when the device supports AL_SOFT_loop_points, and otherwise loop whole.
/// </remarks>
internal sealed class ClipBufferCache(AL al, long budgetBytes, bool supportsLoopPoints) : IDisposable
{
    private const BufferInteger LoopPointsSoft = (BufferInteger)0x2015;

    private readonly Dictionary<SoundClip, ClipBuffer> _buffers = new(ReferenceEqualityComparer.Instance);
    private readonly LinkedList<ClipBuffer> _recent = new();
    private long _totalBytes;

    public ClipBuffer Acquire(SoundClip clip)
    {
        if (_buffers.TryGetValue(clip, out var buffer))
        {
            _recent.Remove(buffer.RecentNode!);
            _recent.AddFirst(buffer.RecentNode!);
        }
        else
        {
            buffer = Upload(clip);
            _buffers.Add(clip, buffer);
            buffer.RecentNode = _recent.AddFirst(buffer);
            _totalBytes += buffer.Bytes;
        }

        buffer.Users++;
        TrimToBudget();
        return buffer;
    }

    public void Release(ClipBuffer buffer)
    {
        buffer.Users--;
        TrimToBudget();
    }

    public void Dispose()
    {
        foreach (var buffer in _buffers.Values)
            al.DeleteBuffer(buffer.Name);
        _buffers.Clear();
        _recent.Clear();
        _totalBytes = 0;
    }

    private unsafe ClipBuffer Upload(SoundClip clip)
    {
        var name = al.GenBuffer();
        var format = clip.Channels == 1 ? BufferFormat.Mono16 : BufferFormat.Stereo16;
        fixed (short* samples = clip.Samples)
            al.BufferData(name, format, samples, clip.Samples.Length * sizeof(short), clip.SampleRate);

        var error = al.GetError();
        if (error != AudioError.NoError)
        {
            al.DeleteBuffer(name);
            throw new InvalidOperationException($"OpenAL could not upload the clip '{clip.Path}': {error}.");
        }

        if (supportsLoopPoints && clip.HasLoopRegion)
        {
            var points = stackalloc int[2];
            points[0] = clip.LoopStart;
            points[1] = clip.LoopEnd > 0 ? clip.LoopEnd : clip.Frames;
            al.SetBufferProperty(name, LoopPointsSoft, points);
            al.GetError();
        }

        return new ClipBuffer(name, clip);
    }

    private void TrimToBudget()
    {
        var node = _recent.Last;
        while (_totalBytes > budgetBytes && node is not null)
        {
            var previous = node.Previous;
            var buffer = node.Value;
            if (buffer.Users == 0)
            {
                _recent.Remove(node);
                _buffers.Remove(buffer.Clip);
                _totalBytes -= buffer.Bytes;
                al.DeleteBuffer(buffer.Name);
            }

            node = previous;
        }
    }
}
