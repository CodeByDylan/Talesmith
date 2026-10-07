using System.Numerics;
using Talesmith.Authoring;
using Talesmith.Ecs;
using Talesmith.Mathematics;
using Talesmith.Rendering;

namespace Talesmith.Runtime.Components;

/// <summary>A camera entity; the active camera with the highest priority renders the scene.</summary>
/// <remarks>In scenes, the view starts at the entity's <see cref="Transform"/> position.</remarks>
[Component(Category = "Rendering", Icon = "video", Description = "Renders the scene; the active camera with the highest priority wins.")]
public struct Camera
{
    [Transient]
    public Camera2D View;

    [Tooltip("The active camera with the highest priority renders the scene.")]
    public int Priority;

    public bool Active;

    /// <summary>An entity with a <see cref="Transform"/> to follow, or <see cref="Entity.Null"/>.</summary>
    [Header("Follow")]
    [Label("Follow target")]
    public Entity Target;

    /// <summary>How quickly the camera catches up with its target; 0 snaps immediately.</summary>
    [Range(0, 50, Step = 0.5)]
    [Tooltip("How quickly the camera catches up with its target; 0 snaps immediately.")]
    public float FollowSharpness;

    /// <summary>When set, keeps the visible area inside these world bounds.</summary>
    [Header("Limits")]
    [Tooltip("Keeps the visible area inside these world bounds.")]
    public Rect2? Bounds;

    [Range(0.01, 64, Step = 0.01)]
    public float MinZoom;

    [Range(0.01, 64, Step = 0.01)]
    public float MaxZoom;

    public Camera()
        : this(Vector2.Zero)
    {
    }

    public Camera(Vector2 position, float zoom = 1)
    {
        View = new Camera2D(position, zoom);
        FollowSharpness = 8;
        MinZoom = 0.05f;
        MaxZoom = 16;
        Active = true;
    }

    /// <summary>Screen pixels per world unit.</summary>
    [Range(0.01, 64, Step = 0.01)]
    public float Zoom
    {
        readonly get => View.Zoom;
        set => View.Zoom = value;
    }

    /// <summary>Rounds the view to whole screen pixels, which keeps pixel art from shimmering.</summary>
    [Tooltip("Rounds the view to whole screen pixels, which keeps pixel art from shimmering.")]
    public bool PixelSnap
    {
        readonly get => View.PixelSnap;
        set => View.PixelSnap = value;
    }
}
