using System.Numerics;
using Avalonia;
using Talesmith.Mathematics;
using Talesmith.Rendering;

namespace Talesmith.Editor.Viewport;

/// <summary>The editor's free camera over the scene: where the view is centered, how far it is zoomed and how screen and world map.</summary>
/// <remarks>
/// Screen coordinates are the viewport's logical pixels with the origin at its top left; world coordinates have Y pointing down, as in the
/// game. Zooming and framing can animate: the camera moves toward its target in <see cref="Advance"/>, which the viewport calls every frame.
/// </remarks>
public sealed class ViewportCamera
{
    public const float MinZoom = 0.02f;
    public const float MaxZoom = 64f;

    private Vector2 _targetPosition;
    private float _targetZoom = 1;
    private (Vector2 Offset, Vector2 World)? _anchor;

    /// <summary>The world point at the center of the view.</summary>
    public Vector2 Position { get; private set; }

    /// <summary>Screen pixels per world unit.</summary>
    public float Zoom { get; private set; } = 1;

    /// <summary>The viewport's size in logical pixels.</summary>
    public Size ViewSize { get; private set; }

    /// <summary>Whether the camera is still moving toward a target.</summary>
    public bool IsAnimating => _targetPosition != Position || Math.Abs(_targetZoom - Zoom) > 1e-6f;

    /// <summary>Raised after the position, zoom or view size changed.</summary>
    public event EventHandler? Changed;

    /// <summary>The camera the game draws through.</summary>
    public Camera2D ToCamera2D() => new(Position, Zoom);

    /// <summary>The world area visible in the view.</summary>
    public Rect2 VisibleWorld
    {
        get
        {
            var topLeft = ScreenToWorld(default);
            return new Rect2(topLeft.X, topLeft.Y, (float)(ViewSize.Width / Zoom), (float)(ViewSize.Height / Zoom));
        }
    }

    public Point WorldToScreen(Vector2 world) =>
        new((world.X - Position.X) * Zoom + ViewSize.Width / 2, (world.Y - Position.Y) * Zoom + ViewSize.Height / 2);

    public Vector2 ScreenToWorld(Point screen) =>
        new((float)((screen.X - ViewSize.Width / 2) / Zoom) + Position.X, (float)((screen.Y - ViewSize.Height / 2) / Zoom) + Position.Y);

    /// <summary>A world rectangle in screen coordinates.</summary>
    public Rect WorldToScreen(Rect2 world)
    {
        var topLeft = WorldToScreen(new Vector2(world.X, world.Y));
        return new Rect(topLeft.X, topLeft.Y, world.Width * Zoom, world.Height * Zoom);
    }

    public void SetViewSize(Size size)
    {
        if (size == ViewSize)
            return;
        ViewSize = size;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Moves and zooms immediately, stopping any animation.</summary>
    public void Set(Vector2 position, float zoom)
    {
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        _targetPosition = position;
        _targetZoom = zoom;
        _anchor = null;
        if (Position == position && Zoom == zoom)
            return;
        Position = position;
        Zoom = zoom;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Moves the view by a distance in screen pixels, as dragging does.</summary>
    public void PanBy(global::Avalonia.Vector screenDelta) =>
        Set(Position - new Vector2((float)(screenDelta.X / Zoom), (float)(screenDelta.Y / Zoom)), Zoom);

    /// <summary>Zooms by a factor while keeping the world point under <paramref name="anchor"/> in place.</summary>
    public void ZoomAt(Point anchor, float factor, bool animate)
    {
        var zoom = Math.Clamp(_targetZoom * factor, MinZoom, MaxZoom);
        var world = ScreenToWorldAt(anchor, _targetPosition, _targetZoom);
        var offset = new Vector2((float)(anchor.X - ViewSize.Width / 2), (float)(anchor.Y - ViewSize.Height / 2));
        var position = world - offset / zoom;
        if (!animate)
        {
            Set(position, zoom);
            return;
        }

        _anchor = (offset, world);
        _targetPosition = position;
        _targetZoom = zoom;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Zooms to an exact level around the center of the view.</summary>
    public void ZoomTo(float zoom, bool animate) =>
        ZoomAt(new Point(ViewSize.Width / 2, ViewSize.Height / 2), zoom / _targetZoom, animate);

    /// <summary>Centers and zooms the view so a world rectangle fills it with a margin.</summary>
    public void Frame(Rect2 world, bool animate, double margin = 48)
    {
        var width = Math.Max(1, ViewSize.Width - margin * 2);
        var height = Math.Max(1, ViewSize.Height - margin * 2);
        var zoom = world.Width <= 0 || world.Height <= 0
            ? _targetZoom
            : (float)Math.Min(width / Math.Max(world.Width, 1e-3), height / Math.Max(world.Height, 1e-3));
        zoom = Math.Clamp(zoom, MinZoom, 8);
        var center = new Vector2(world.X + world.Width / 2, world.Y + world.Height / 2);
        if (animate)
            AnimateTo(center, zoom);
        else
            Set(center, zoom);
    }

    public void AnimateTo(Vector2 position, float zoom)
    {
        _targetPosition = position;
        _targetZoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        _anchor = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Moves toward the target; returns whether the camera moved.</summary>
    public bool Advance(TimeSpan elapsed)
    {
        if (!IsAnimating)
            return false;
        var t = 1 - MathF.Exp(-(float)elapsed.TotalSeconds * 18);
        var zoom = MathF.Exp(float.Lerp(MathF.Log(Zoom), MathF.Log(_targetZoom), t));
        var position = _anchor is var (offset, world) ? world - offset / zoom : Vector2.Lerp(Position, _targetPosition, t);
        if (Math.Abs(zoom - _targetZoom) / _targetZoom < 0.002f && Vector2.Distance(position, _targetPosition) * zoom < 0.25f)
        {
            zoom = _targetZoom;
            position = _targetPosition;
            _anchor = null;
        }

        Position = position;
        Zoom = zoom;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private Vector2 ScreenToWorldAt(Point screen, Vector2 position, float zoom) =>
        new((float)((screen.X - ViewSize.Width / 2) / zoom) + position.X, (float)((screen.Y - ViewSize.Height / 2) / zoom) + position.Y);
}
