namespace Talesmith.Audio.OpenAL;

/// <summary>One pooled OpenAL source for sound effects and the clip it is playing.</summary>
internal sealed class Voice(int index, uint source)
{
    public int Index { get; } = index;

    public uint Source { get; } = source;

    /// <summary>Increases every time the voice starts a sound, so handles to earlier sounds stop matching.</summary>
    public int Generation { get; set; }

    /// <summary>The clip being played, or null when the voice is free.</summary>
    public ClipBuffer? Buffer { get; set; }

    public AudioBus Bus { get; set; }

    public float Volume { get; set; }

    public bool Loop { get; set; }

    /// <summary>When the sound started relative to others, for stealing the oldest voice when all are busy.</summary>
    public long StartOrder { get; set; }

    public bool IsActive => Buffer is not null;
}
