using System.Numerics;
using System.Text.Json.Nodes;
using Talesmith.Ecs;
using Talesmith.Editor.Projects;
using Talesmith.Mathematics;
using Talesmith.Rendering;
using Talesmith.Runtime.Audio;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Hosting;
using Talesmith.Runtime.Serialization;
using Talesmith.VFX;

namespace Talesmith.Editor.Viewport.Gizmos;

/// <summary>Draws what cameras see: the view rectangle at the camera's zoom, with corner marks and the camera's name, and its bounds when it has
/// some.</summary>
/// <remarks>
/// With a scale mode, the rectangle is the design size: exactly what fit shows, the least that expand shows and the most that crop shows. For
/// expand and crop a faint dashed rectangle adds what the game's window size shows. Without one, it is the window size.
/// </remarks>
public sealed class CameraGizmo(IProjectService project) : IGizmoProvider
{
    private static readonly QueryDescription Cameras = QueryDescription.With<Camera>().And<Transform>().Without<Inactive>();

    public void Draw(GizmoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var settings = project.Settings;
        var color = context.Palette.Camera;
        foreach (var archetype in context.World.Query(Cameras))
        {
            var cameras = archetype.GetSpan<Camera>();
            var transforms = archetype.GetSpan<Transform>();
            for (var i = 0; i < archetype.Count; i++)
            {
                var entity = archetype.Entities[i];
                var selected = context.IsSelected(entity);
                var opacity = selected ? 0.95 : context.IsHovered(entity) ? 0.7 : 0.4;
                var zoom = Math.Max(cameras[i].Zoom, 0.01f);
                var (view, atWindowSize) = Areas(settings, transforms[i].Position, zoom);
                if (atWindowSize is { } window)
                    context.Rectangle(window, color, 1, opacity * 0.35, dashed: true);
                context.Rectangle(view, color, selected ? 1.5 : 1, opacity * 0.7);
                Corners(context, view, color, opacity);
                var name = context.World.TryGet<Name>(entity, out var named) && !string.IsNullOrEmpty(named.Value) ? named.Value : "Camera";
                context.Label(new Vector2(view.X, view.Y), $"{name}  {SizeText(settings)} @ {zoom:0.##}×", color, opacity);
                if (cameras[i].Bounds is { } bounds)
                    context.Rectangle(bounds, context.Palette.Accent, 1.25, opacity, dashed: true);
            }
        }
    }

    /// <summary>The world area a camera shows, and for scale modes whose visible area depends on the window, what the game's window size
    /// shows when it differs.</summary>
    public static (Rect2 View, Rect2? AtWindowSize) Areas(GameSettings settings, Vector2 center, float zoom)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var view = settings.View;
        var windowView = ViewLayout.Compute(new Vector2(settings.WindowWidth, settings.WindowHeight), 1, view).ViewSize / zoom;
        if (view.ScaleMode == ViewScaleMode.None)
            return (Rect2.FromCenter(center, windowView), null);
        var design = new Vector2(view.Width, view.Height) / zoom;
        var varies = view.ScaleMode != ViewScaleMode.Fit && Vector2.Distance(windowView, design) > 0.5f / zoom;
        return (Rect2.FromCenter(center, design), varies ? Rect2.FromCenter(center, windowView) : null);
    }

    private static string SizeText(GameSettings settings) => settings.View.ScaleMode == ViewScaleMode.None
        ? $"{settings.WindowWidth}×{settings.WindowHeight}"
        : $"{settings.View.Width}×{settings.View.Height} {settings.View.ScaleMode.ToString().ToLowerInvariant()}";

    private static void Corners(GizmoContext context, Rect2 view, global::Avalonia.Media.Color color, double opacity)
    {
        var mark = Math.Min(18 * context.PixelSize, Math.Min(view.Width, view.Height) / 4);
        Span<Vector2> corners = [new(view.X, view.Y), new(view.X + view.Width, view.Y), new(view.X + view.Width, view.Y + view.Height), new(view.X, view.Y + view.Height)];
        Span<Vector2> inward = [new(1, 1), new(-1, 1), new(-1, -1), new(1, -1)];
        for (var i = 0; i < 4; i++)
        {
            context.Line(corners[i], corners[i] + new Vector2(inward[i].X * mark, 0), color, 2.5, opacity);
            context.Line(corners[i], corners[i] + new Vector2(0, inward[i].Y * mark), color, 2.5, opacity);
        }
    }
}

/// <summary>Draws particle emitters: the emission shape, the direction of fixed emission and the live bounds of the particles, with handles for the
/// shape's radius and size.</summary>
public sealed class ParticleGizmo : IGizmoProvider
{
    private static readonly QueryDescription Emitters = QueryDescription.With<ParticleEmitter>().And<Transform>().Without<Inactive>();
    private readonly string _type = ComponentRegistry.GetTypeName(typeof(ParticleEmitter));

    public void Draw(GizmoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var color = context.Palette.Particles;
        foreach (var archetype in context.World.Query(Emitters))
        {
            var emitters = archetype.GetSpan<ParticleEmitter>();
            var transforms = archetype.GetSpan<Transform>();
            for (var i = 0; i < archetype.Count; i++)
            {
                var entity = archetype.Entities[i];
                var selected = context.IsSelected(entity);
                if (!selected && !context.IsHovered(entity))
                    continue;
                var emitter = emitters[i];
                var transform = transforms[i];
                var settings = emitter.Simulation.Settings ?? emitter.Settings;
                Shape(context, settings.Shape, transform, color);
                if (emitter.Simulation.Bounds is { Width: > 0, Height: > 0 } bounds)
                {
                    context.Rectangle(bounds, color, 1, 0.55, dashed: true);
                    context.Label(new Vector2(bounds.X, bounds.Y), $"{emitter.AliveCount} particles", color, 0.8);
                }
            }
        }
    }

    public void CollectHandles(GizmoContext context, ICollection<GizmoHandle> handles)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(handles);
        foreach (var archetype in context.World.Query(Emitters))
        {
            var emitters = archetype.GetSpan<ParticleEmitter>();
            var transforms = archetype.GetSpan<Transform>();
            for (var i = 0; i < archetype.Count; i++)
            {
                var entity = archetype.Entities[i];
                if (!context.IsSelected(entity) || !emitters[i].Preset.IsEmpty || GizmoMath.DocumentId(context.World, entity) is var id && id == Guid.Empty)
                    continue;
                var shape = emitters[i].Settings.Shape;
                var kind = shape.Kind switch
                {
                    ParticleShapeKind.Circle or ParticleShapeKind.Cone => ShapeKind.Circle,
                    ParticleShapeKind.Rectangle => ShapeKind.Box,
                    _ => (ShapeKind?)null
                };
                if (kind is not { } shapeKind)
                    continue;
                ShapeHandles.Add(handles, id, _type, shapeKind, transforms[i], shape.Offset, shape.Rotation, shape.Size, shape.Radius, null, context.Palette.Particles,
                    "settings.shape.");
            }
        }
    }

    private static void Shape(GizmoContext context, ShapeModule shape, in Transform transform, global::Avalonia.Media.Color color)
    {
        var t = transform;
        Vector2 World(Vector2 local) => GizmoMath.ToWorld(t, shape.Offset + GizmoMath.Rotate(local, shape.Rotation));
        var center = World(Vector2.Zero);
        switch (shape.Kind)
        {
            case ParticleShapeKind.Line:
                context.Line(World(new Vector2(-shape.Size.X / 2, 0)), World(new Vector2(shape.Size.X / 2, 0)), color, 2);
                break;
            case ParticleShapeKind.Rectangle or ParticleShapeKind.Tile:
            {
                var half = (shape.Kind == ParticleShapeKind.Tile ? shape.CellSize : shape.Size) / 2;
                Span<Vector2> corners = [World(new(-half.X, -half.Y)), World(new(half.X, -half.Y)), World(new(half.X, half.Y)), World(new(-half.X, half.Y))];
                context.Polyline(corners, color, closed: true, 1.5, 1, fillOpacity: 0.06);
                break;
            }
            case ParticleShapeKind.Circle:
            {
                var start = transform.Rotation + shape.Rotation;
                if (shape.Arc >= MathF.Tau - 0.001f)
                    context.Circle(center, shape.Radius, color, 1.5, 1, fillOpacity: 0.05);
                else
                    context.Arc(center, shape.Radius, start, shape.Arc, color);
                if (shape.InnerRadius > 0)
                    context.Circle(center, shape.InnerRadius, color, 1.25, 0.7, dashed: true);
                break;
            }
            case ParticleShapeKind.Cone:
            {
                var axis = transform.Rotation + shape.Rotation + shape.Angle;
                var length = shape.Radius * 3;
                var spread = shape.ConeAngle / 2;
                context.Line(center + GizmoMath.Direction(axis + MathF.PI / 2) * shape.Radius, center + GizmoMath.Direction(axis + MathF.PI / 2) * shape.Radius + GizmoMath.Direction(axis + spread) * length, color, 1.5);
                context.Line(center - GizmoMath.Direction(axis + MathF.PI / 2) * shape.Radius, center - GizmoMath.Direction(axis + MathF.PI / 2) * shape.Radius + GizmoMath.Direction(axis - spread) * length, color, 1.5);
                context.Line(center + GizmoMath.Direction(axis + MathF.PI / 2) * shape.Radius, center - GizmoMath.Direction(axis + MathF.PI / 2) * shape.Radius, color, 2);
                break;
            }
            case ParticleShapeKind.Polygon when shape.Points.Count >= 2:
            {
                var points = new Vector2[shape.Points.Count];
                for (var i = 0; i < points.Length; i++)
                    points[i] = World(shape.Points[i]);
                context.Polyline(points, color, closed: true, 1.5, 1, fillOpacity: 0.06);
                break;
            }
        }

        context.Dot(center, color, 3);
        if (shape.Direction == ParticleDirectionMode.Fixed && shape.Kind != ParticleShapeKind.Cone)
        {
            var direction = GizmoMath.Direction(transform.Rotation + shape.Angle);
            var end = center + direction * 36 * context.PixelSize;
            context.Line(center, end, color, 1.5, 0.9);
            var normal = new Vector2(-direction.Y, direction.X) * 4 * context.PixelSize;
            context.Line(end, end - direction * 6 * context.PixelSize + normal, color, 1.5, 0.9);
            context.Line(end, end - direction * 6 * context.PixelSize - normal, color, 1.5, 0.9);
        }
    }
}

/// <summary>Draws the minimum and maximum distance of spatial audio sources, with handles to change them.</summary>
public sealed class AudioGizmo : IGizmoProvider
{
    private static readonly QueryDescription Sources = QueryDescription.With<AudioSource>().And<Transform>().Without<Inactive>();
    private readonly string _type = ComponentRegistry.GetTypeName(typeof(AudioSource));

    public void Draw(GizmoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var color = context.Palette.Audio;
        foreach (var archetype in context.World.Query(Sources))
        {
            var sources = archetype.GetSpan<AudioSource>();
            var transforms = archetype.GetSpan<Transform>();
            for (var i = 0; i < archetype.Count; i++)
            {
                var entity = archetype.Entities[i];
                var selected = context.IsSelected(entity);
                if (!sources[i].Spatial || !selected && !context.IsHovered(entity))
                    continue;
                var position = transforms[i].Position;
                context.Circle(position, sources[i].MinDistance, color, 1.5, 1, fillOpacity: 0.05);
                context.Circle(position, sources[i].MaxDistance, color, 1.25, 0.7, dashed: true);
            }
        }
    }

    public void CollectHandles(GizmoContext context, ICollection<GizmoHandle> handles)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(handles);
        foreach (var archetype in context.World.Query(Sources))
        {
            var sources = archetype.GetSpan<AudioSource>();
            var transforms = archetype.GetSpan<Transform>();
            for (var i = 0; i < archetype.Count; i++)
            {
                var entity = archetype.Entities[i];
                if (!sources[i].Spatial || !context.IsSelected(entity) || GizmoMath.DocumentId(context.World, entity) is var id && id == Guid.Empty)
                    continue;
                var position = transforms[i].Position;
                foreach (var (path, distance, key) in new[] { ("minDistance", sources[i].MinDistance, "min"), ("maxDistance", sources[i].MaxDistance, "max") })
                {
                    handles.Add(new GizmoHandle(id, _type, path, position + new Vector2(distance, 0),
                        drag => JsonValue.Create(GizmoMath.Snap(Vector2.Distance(drag.World, position), 8, drag.Snap)))
                    {
                        Shape = GizmoHandleShape.Circle,
                        Color = context.Palette.Audio,
                        Key = key,
                        Hint = key == "min" ? "Drag to change the distance of full volume" : "Drag to change the distance where the sound fades out"
                    });
                }
            }
        }
    }
}
