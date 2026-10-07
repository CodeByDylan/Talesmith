namespace Talesmith.Audio.OpenAL;

/// <summary>An OpenAL buffer holding the samples of one <see cref="SoundClip"/>.</summary>
internal sealed class ClipBuffer(uint name, SoundClip clip)
{
    public uint Name { get; } = name;

    public SoundClip Clip { get; } = clip;

    public long Bytes => Clip.Samples.Length * (long)sizeof(short);

    /// <summary>The number of voices playing this buffer; it cannot be deleted while above zero.</summary>
    public int Users { get; set; }

    public LinkedListNode<ClipBuffer>? RecentNode { get; set; }
}
