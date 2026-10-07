using System.Numerics;
using Talesmith.Mathematics;

namespace Talesmith.Rendering;

/// <summary>Where a game's view is drawn inside its render target, and how target pixels, view units and the world relate.</summary>
/// <remarks>
/// <para>Target pixels are the device pixels of the render target with (0, 0) at its top-left, such as mouse positions. View units are the
/// game's design units: <see cref="RenderSpace.Screen"/> draws use them with (0, 0) at the top-left of <see cref="ViewRect"/>, and a camera
/// with zoom 1 shows one world unit per view unit.</para>
/// <para>Positions outside the view rectangle, such as a click on the bars, are not clamped: they map to view units below 0 or beyond
/// <see cref="ViewSize"/>, and to world positions beyond the visible area.</para>
/// </remarks>
/// <param name="TargetSize">The whole render target in device pixels.</param>
/// <param name="ViewRect">The part of the target the game is drawn in, in device pixels with whole-pixel edges.</param>
/// <param name="Scale">Device pixels per view unit; frames multiply camera zoom by it.</param>
/// <param name="ViewSize">The size of the view in view units.</param>
public readonly record struct ViewLayout(Vector2 TargetSize, Rect2 ViewRect, float Scale, Vector2 ViewSize)
{
    /// <summary>A layout that draws on the whole target with <paramref name="scale"/> device pixels per view unit.</summary>
    public static ViewLayout Whole(Vector2 targetSize, float scale = 1) =>
        new(targetSize, new Rect2(0, 0, targetSize.X, targetSize.Y), scale, targetSize / scale);

    /// <summary>Lays out a view on a render target.</summary>
    /// <remarks>
    /// <para><see cref="ViewScaleMode.None"/> draws on the whole target at <paramref name="displayScale"/>, so view units are logical pixels.
    /// The other modes ignore the display scale: <see cref="ViewScaleMode.Fit"/> scales the design size to fit and centers it, leaving bars;
    /// <see cref="ViewScaleMode.Expand"/> uses the same scale on the whole target; <see cref="ViewScaleMode.Crop"/> scales the design size to
    /// cover the whole target.</para>
    /// <para>With <see cref="ViewSettings.IntegerScale"/> a scale of 1 or more is rounded down to a whole number, or up for
    /// <see cref="ViewScaleMode.Crop"/> so the view still covers the target; a scale below 1 is used as it is, because no whole number fits.
    /// In <see cref="ViewScaleMode.Fit"/> the pixels left over by rounding become part of the bars.</para>
    /// </remarks>
    /// <param name="targetSize">The render target in device pixels; it is rounded to whole pixels, at least 1×1.</param>
    /// <param name="displayScale">Device pixels per logical pixel, such as 2 on a high-density display.</param>
    public static ViewLayout Compute(Vector2 targetSize, float displayScale, ViewSettings view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var target = new Vector2(Math.Max(1, MathF.Round(targetSize.X)), Math.Max(1, MathF.Round(targetSize.Y)));
        var design = view.DesignSize;
        var fit = MathF.Min(target.X / design.X, target.Y / design.Y);
        switch (view.ScaleMode)
        {
            case ViewScaleMode.Fit:
            {
                var scale = Rounded(fit, view.IntegerScale, up: false);
                var size = Vector2.Clamp(new Vector2(MathF.Round(design.X * scale), MathF.Round(design.Y * scale)), Vector2.One, target);
                var position = new Vector2(MathF.Floor((target.X - size.X) / 2), MathF.Floor((target.Y - size.Y) / 2));
                return new ViewLayout(target, new Rect2(position.X, position.Y, size.X, size.Y), scale, design);
            }
            case ViewScaleMode.Expand:
                return Whole(target, Rounded(fit, view.IntegerScale, up: false));
            case ViewScaleMode.Crop:
                return Whole(target, Rounded(MathF.Max(target.X / design.X, target.Y / design.Y), view.IntegerScale, up: true));
            default:
                var display = float.IsFinite(displayScale) && displayScale > 0 ? displayScale : 1;
                return Whole(target, Rounded(display, view.IntegerScale, up: false));
        }
    }

    /// <summary>Whether the view leaves part of the target for the bars.</summary>
    public bool HasBorders => ViewRect.X > 0 || ViewRect.Y > 0 || ViewRect.Width < TargetSize.X || ViewRect.Height < TargetSize.Y;

    /// <summary>Whether a target pixel position, such as the mouse, is on the view rather than on the bars around it.</summary>
    public bool Contains(Vector2 target) => ViewRect.Contains(target);

    /// <summary>Converts a target pixel position, such as the mouse, to view units.</summary>
    public Vector2 TargetToView(Vector2 target) => (target - ViewRect.Position) / Scale;

    public Vector2 ViewToTarget(Vector2 view) => view * Scale + ViewRect.Position;

    /// <summary>The camera as frames draw it: its zoom multiplied by <see cref="Scale"/>, in device pixels per world unit.</summary>
    public Camera2D ScaleCamera(in Camera2D camera) => camera with { Zoom = camera.Zoom * Scale };

    /// <param name="camera">A camera from <see cref="ScaleCamera"/>, such as <see cref="RenderFrame.Camera"/>.</param>
    public Vector2 TargetToWorld(in Camera2D camera, Vector2 target) => camera.ScreenToWorld(target - ViewRect.Position, ViewRect.Size);

    /// <param name="camera">A camera from <see cref="ScaleCamera"/>, such as <see cref="RenderFrame.Camera"/>.</param>
    public Vector2 WorldToTarget(in Camera2D camera, Vector2 world) => camera.WorldToScreen(world, ViewRect.Size) + ViewRect.Position;

    /// <param name="camera">A camera from <see cref="ScaleCamera"/>, such as <see cref="RenderFrame.Camera"/>.</param>
    public Vector2 ViewToWorld(in Camera2D camera, Vector2 view) => camera.ScreenToWorld(view * Scale, ViewRect.Size);

    /// <param name="camera">A camera from <see cref="ScaleCamera"/>, such as <see cref="RenderFrame.Camera"/>.</param>
    public Vector2 WorldToView(in Camera2D camera, Vector2 world) => camera.WorldToScreen(world, ViewRect.Size) / Scale;

    /// <summary>The world area visible in the view rectangle.</summary>
    /// <param name="camera">A camera from <see cref="ScaleCamera"/>, such as <see cref="RenderFrame.Camera"/>.</param>
    public Rect2 VisibleWorld(in Camera2D camera) => camera.VisibleBounds(ViewRect.Size);

    private static float Rounded(float scale, bool integer, bool up) =>
        !integer || scale < 1 ? scale : up ? MathF.Ceiling(scale) : MathF.Floor(scale);
}
