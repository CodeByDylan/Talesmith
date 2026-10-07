namespace Talesmith.Audio;

/// <summary>How an audio file is kept in memory.</summary>
public enum AudioLoadType
{
    /// <summary>Sound clips are decoded completely; music streams from the file while it plays.</summary>
    Auto,

    /// <summary>The whole file is read into memory when imported; music is still decoded while it plays, but never touches the disk.</summary>
    Preload,

    /// <summary>Music streams from the file while it plays; sound clips are always decoded completely.</summary>
    Stream
}

/// <summary>Import settings of audio files, stored in their .meta files and shared by sound clips and music tracks.</summary>
public sealed record AudioImportSettings
{
    public static AudioImportSettings Default { get; } = new();

    public AudioLoadType LoadType { get; init; } = AudioLoadType.Auto;

    /// <summary>Whether the sound loops when played without options that say otherwise.</summary>
    public bool Loop { get; init; }

    /// <summary>Where looping restarts, in seconds from the start; the part before it plays once, as an intro.</summary>
    public double LoopStart { get; init; }

    /// <summary>Where looping jumps back to <see cref="LoopStart"/>, in seconds; null loops at the end.</summary>
    public double? LoopEnd { get; init; }

    /// <summary>The bus the sound plays on when played without options; music always plays on <see cref="AudioBus.Music"/>.</summary>
    public AudioBus Bus { get; init; } = AudioBus.Effects;

    /// <summary>A volume multiplier from 0 to 1 applied every time the sound plays.</summary>
    public float Volume { get; init; } = 1;
}
