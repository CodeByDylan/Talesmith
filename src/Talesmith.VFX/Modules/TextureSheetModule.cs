using Talesmith.Authoring;

namespace Talesmith.VFX;

/// <summary>How a texture sheet advances through its frames.</summary>
public enum TextureSheetMode
{
    /// <summary>Plays the frames over each particle's lifetime, <see cref="TextureSheetModule.Cycles"/> times.</summary>
    OverLifetime,

    /// <summary>Plays the frames at <see cref="TextureSheetModule.FramesPerSecond"/>, looping.</summary>
    FramesPerSecond
}

/// <summary>Animates particles through a grid of frames in their texture, such as flames or smoke puffs.</summary>
public sealed class TextureSheetModule
{
    public bool Enabled;

    [Tooltip("Frames across the texture.")]
    [Range(1, 64, Step = 1)]
    public int Columns = 4;

    [Tooltip("Frames down the texture.")]
    [Range(1, 64, Step = 1)]
    public int Rows = 4;

    [Label("Frame count")]
    [Tooltip("Frames used, left to right and top to bottom; 0 uses every frame.")]
    [Range(0, 4096, Step = 1)]
    public int FrameCount;

    public TextureSheetMode Mode = TextureSheetMode.OverLifetime;

    [Label("Frames per second")]
    [Range(0, 240)]
    public float FramesPerSecond = 12;

    [Tooltip("How many times the frames play over a lifetime.")]
    [Range(0.01, 100)]
    public float Cycles = 1;

    [Label("Random start frame")]
    [Tooltip("Starts each particle on a random frame, so neighbors do not animate in step.")]
    public bool RandomStartFrame;
}
