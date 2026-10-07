using System.Numerics;
using Talesmith.Mathematics;

namespace Talesmith.Rendering;

/// <summary>How the game's view fills a window whose size and shape differ from the design size.</summary>
public enum ViewScaleMode
{
    /// <summary>No scaling: the view is the whole window at the display's scale, so a larger window shows more of the world.</summary>
    None,

    /// <summary>Shows exactly the design size, as large as fits, and fills the rest of the window with bars.</summary>
    Fit,

    /// <summary>Shows at least the design size, as large as fits, and more of the world along the window's longer side.</summary>
    Expand,

    /// <summary>Shows at most the design size, filling the window and cutting off what does not fit along its longer side.</summary>
    Crop
}

/// <summary>The size a game is designed for and how it scales to the window: the <c>view</c> section of <c>config/game.json</c>.</summary>
public sealed record ViewSettings
{
    /// <summary>The settings of games without a <c>view</c> section: <see cref="ViewScaleMode.None"/>.</summary>
    public static ViewSettings Unscaled { get; } = new() { ScaleMode = ViewScaleMode.None };

    /// <summary>The design width in view units; ignored by <see cref="ViewScaleMode.None"/>.</summary>
    public int Width { get; init; } = 1280;

    /// <summary>The design height in view units; ignored by <see cref="ViewScaleMode.None"/>.</summary>
    public int Height { get; init; } = 720;

    public ViewScaleMode ScaleMode { get; init; } = ViewScaleMode.Fit;

    /// <summary>Scales by whole numbers only, which keeps pixel art crisp; see <see cref="ViewLayout.Compute"/> for how the scale is rounded.</summary>
    public bool IntegerScale { get; init; }

    /// <summary>The color of the bars around the view where it does not fill the window.</summary>
    public Color BorderColor { get; init; } = Color.Black;

    /// <summary>The width overlays are designed for in overlay units, or null to lay them out in view units; set together with <see cref="OverlayHeight"/>.</summary>
    public int? OverlayWidth { get; init; }

    /// <summary>The height overlays are designed for in overlay units, or null to lay them out in view units; set together with <see cref="OverlayWidth"/>.</summary>
    public int? OverlayHeight { get; init; }

    /// <summary>Describes what makes the settings unusable, or returns null when they are valid.</summary>
    public string? FindError()
    {
        if (!Enum.IsDefined(ScaleMode))
            return $"The view scale mode {(int)ScaleMode} is not known; use fit, expand, crop or none.";
        if (Width <= 0)
            return $"The view width must be positive, but is {Width}.";
        if (Height <= 0)
            return $"The view height must be positive, but is {Height}.";
        if (OverlayWidth.HasValue != OverlayHeight.HasValue)
            return OverlayWidth.HasValue
                ? "The view has an overlay width but no overlay height; set both or neither."
                : "The view has an overlay height but no overlay width; set both or neither.";
        if (OverlayWidth <= 0)
            return $"The view overlay width must be positive, but is {OverlayWidth}.";
        if (OverlayHeight <= 0)
            return $"The view overlay height must be positive, but is {OverlayHeight}.";
        return null;
    }

    /// <summary>Overlay units per view unit: the smaller of the overlay size divided by the design size along each axis, so in
    /// <see cref="ViewScaleMode.Fit"/> overlays lay out at exactly the overlay size.</summary>
    /// <remarks>It is 1 without a valid overlay size and in <see cref="ViewScaleMode.None"/>, where overlays lay out in view units.</remarks>
    public float OverlayUnitsPerViewUnit() =>
        ScaleMode == ViewScaleMode.None || OverlayWidth is not { } width || OverlayHeight is not { } height || FindError() is not null
            ? 1
            : MathF.Min((float)width / Width, (float)height / Height);

    internal Vector2 DesignSize => new(Width, Height);
}
