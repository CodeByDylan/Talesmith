using System.Numerics;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Talesmith.Assets.Textures;

namespace Talesmith.Editor.Assets.SpriteEditor;

/// <summary>A slice in the sprite editor's list and canvas.</summary>
public sealed partial class SliceItem(SliceState state) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(Rect), nameof(Pivot), nameof(SizeText))]
    private SliceState _state = state;

    [ObservableProperty]
    private bool _isSelected;

    public string Name => State.Name;

    public PixelRect Rect => State.Rect;

    public Vector2 Pivot => State.Pivot;

    public string SizeText => $"{Rect.Width}×{Rect.Height}";
}

/// <summary>An animation in the sprite editor's list.</summary>
public sealed partial class AnimationItem(AnimationState state) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(FrameText))]
    private AnimationState _state = state;

    public string Name => State.Name;

    public string FrameText => State.Frames.Count == 1 ? "1 frame" : $"{State.Frames.Count} frames";
}

/// <summary>A frame of the selected animation, with its sprite cut from the texture.</summary>
public sealed record FrameItem(int Index, string Name, CroppedBitmap? Image, bool IsMissing)
{
    public string Number => (Index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Where new slices put their pivot.</summary>
public sealed record PivotPreset(string Name, Vector2 Pivot)
{
    public static IReadOnlyList<PivotPreset> All { get; } =
    [
        new("Center", new Vector2(0.5f, 0.5f)),
        new("Bottom center", new Vector2(0.5f, 1)),
        new("Top left", new Vector2(0, 0)),
        new("Top center", new Vector2(0.5f, 0)),
        new("Top right", new Vector2(1, 0)),
        new("Left center", new Vector2(0, 0.5f)),
        new("Right center", new Vector2(1, 0.5f)),
        new("Bottom left", new Vector2(0, 1)),
        new("Bottom right", new Vector2(1, 1))
    ];

    public override string ToString() => Name;
}
