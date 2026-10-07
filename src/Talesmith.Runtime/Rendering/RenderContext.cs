using System.Numerics;
using Talesmith.Rendering;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Runtime.Rendering;

/// <summary>Gives systems access to the frame being built, the camera, the view and conversions between them.</summary>
/// <remarks>
/// <para><see cref="Frame"/> is available to systems in the <see cref="Talesmith.Systems.SystemPhase.PreRender"/> phase, which draw into it.</para>
/// <para>Screen positions are device pixels of the game view with (0, 0) at its top-left, such as mouse positions. View units are what
/// <see cref="RenderSpace.Screen"/> draws use; see <see cref="ViewLayout"/>. Positions on the bars around the view are not clamped.</para>
/// </remarks>
public sealed class RenderContext(Viewport viewport, TextureCache textures)
{
    private RenderFrame? _frame;

    public Viewport Viewport { get; } = viewport;

    public TextureCache Textures { get; } = textures;

    /// <summary>The camera of the frame being built, or of the last frame, with its zoom in device pixels per world unit.</summary>
    public Camera2D Camera { get; private set; } = new();

    /// <summary>The layout of the frame being built, or of the last frame.</summary>
    public ViewLayout View { get; private set; } = viewport.Layout;

    /// <summary>The frame being built.</summary>
    /// <exception cref="InvalidOperationException">No frame is being built; draw only from PreRender systems.</exception>
    public RenderFrame Frame => _frame ?? throw new InvalidOperationException("No frame is being built. Draw from systems in the PreRender phase.");

    /// <summary>Converts a screen position, such as the mouse, to world space using the current camera.</summary>
    public Vector2 ScreenToWorld(Vector2 screen) => View.TargetToWorld(Camera, screen);

    /// <summary>Converts a world position to a screen position in device pixels.</summary>
    public Vector2 WorldToScreen(Vector2 world) => View.WorldToTarget(Camera, world);

    /// <summary>Converts a screen position, such as the mouse, to view units, the space of HUDs drawn in <see cref="RenderSpace.Screen"/>.</summary>
    public Vector2 ScreenToView(Vector2 screen) => View.TargetToView(screen);

    /// <summary>Converts a world position to view units, for example to place a HUD marker over an entity.</summary>
    public Vector2 WorldToView(Vector2 world) => View.WorldToView(Camera, world);

    /// <summary>The world area visible through the current camera.</summary>
    public Mathematics.Rect2 VisibleBounds => View.VisibleWorld(Camera);

    internal void Begin(RenderFrame frame)
    {
        _frame = frame;
        Camera = frame.Camera;
        View = frame.View;
    }

    internal void End() => _frame = null;
}
