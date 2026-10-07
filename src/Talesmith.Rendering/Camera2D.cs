using System.Numerics;
using Talesmith.Mathematics;

namespace Talesmith.Rendering;

/// <summary>A 2D camera: the world point at the center of the view, a zoom and a rotation.</summary>
public struct Camera2D
{
    public Camera2D()
    {
    }

    public Camera2D(Vector2 position, float zoom = 1)
    {
        Position = position;
        Zoom = zoom;
    }

    /// <summary>The world point shown at the center of the viewport.</summary>
    public Vector2 Position;

    /// <summary>Screen pixels per world unit.</summary>
    public float Zoom = 1;

    /// <summary>Clockwise rotation of the view in radians.</summary>
    public float Rotation;

    /// <summary>Rounds the view to whole screen pixels, which keeps pixel art from shimmering while the camera moves.</summary>
    public bool PixelSnap;

    /// <summary>The transform from world space to screen pixels for a viewport of the given size.</summary>
    public readonly Matrix3x2 ViewMatrix(Vector2 viewportSize)
    {
        var position = PixelSnap ? new Vector2(MathF.Round(Position.X * Zoom) / Zoom, MathF.Round(Position.Y * Zoom) / Zoom) : Position;
        var view = Matrix3x2.CreateTranslation(-position);
        if (Rotation != 0)
            view *= Matrix3x2.CreateRotation(-Rotation);
        view *= Matrix3x2.CreateScale(Zoom);
        var center = viewportSize / 2;
        if (PixelSnap)
            center = new Vector2(MathF.Round(center.X), MathF.Round(center.Y));
        return view * Matrix3x2.CreateTranslation(center);
    }

    /// <summary>The world-space area visible in a viewport, as an axis-aligned rectangle (larger than the view when rotated).</summary>
    public readonly Rect2 VisibleBounds(Vector2 viewportSize)
    {
        Matrix3x2.Invert(ViewMatrix(viewportSize), out var inverse);
        return new Rect2(0, 0, viewportSize.X, viewportSize.Y).Transform(inverse);
    }

    public readonly Vector2 ScreenToWorld(Vector2 screen, Vector2 viewportSize)
    {
        Matrix3x2.Invert(ViewMatrix(viewportSize), out var inverse);
        return Vector2.Transform(screen, inverse);
    }

    public readonly Vector2 WorldToScreen(Vector2 world, Vector2 viewportSize) => Vector2.Transform(world, ViewMatrix(viewportSize));
}
