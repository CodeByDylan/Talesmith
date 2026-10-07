using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Talesmith.Ecs;
using Talesmith.Mathematics;
using Color = Avalonia.Media.Color;

namespace Talesmith.Editor.Viewport.Gizmos;

/// <summary>The colors of gizmos: one per kind, matching the theme, so the same thing always looks the same.</summary>
public sealed class GizmoPalette
{
    private static GizmoPalette? _cached;
    private static (Color Accent, bool Light) _cachedKey;

    private GizmoPalette(Color accent, bool light)
    {
        Accent = accent;
        IsLight = light;
    }

    public Color Accent { get; }

    public bool IsLight { get; }

    public Color AxisX { get; } = Color.Parse("#EF5350");

    public Color AxisY { get; } = Color.Parse("#5CC46C");

    /// <summary>The color of a handle under the pointer or being dragged.</summary>
    public Color Highlight { get; } = Color.Parse("#FFD54A");

    public Color Light { get; } = Color.Parse("#FFC857");

    public Color Collider { get; } = Color.Parse("#4ADE80");

    public Color Trigger { get; } = Color.Parse("#38BDF8");

    public Color Shadow { get; } = Color.Parse("#A78BFA");

    public Color Audio { get; } = Color.Parse("#60A5FA");

    public Color Particles { get; } = Color.Parse("#F472B6");

    public Color Camera => IsLight ? Color.Parse("#3F4452") : Color.Parse("#E6E8EE");

    /// <summary>The outline around handles that sets them off from any background.</summary>
    public Color HandleOutline { get; } = Color.FromArgb(200, 12, 13, 16);

    /// <summary>The palette for the current accent and theme.</summary>
    public static GizmoPalette Current
    {
        get
        {
            var app = Application.Current;
            var accent = app?.TryGetResource("AccentColor", app.ActualThemeVariant, out var value) == true && value is Color color ? color : Color.Parse("#7073F6");
            var light = app?.ActualThemeVariant == global::Avalonia.Styling.ThemeVariant.Light;
            if (_cached is null || _cachedKey != (accent, light))
            {
                _cached = new GizmoPalette(accent, light);
                _cachedKey = (accent, light);
            }

            return _cached;
        }
    }
}

/// <summary>What a gizmo provider draws with: the viewport's camera, the edit world, the selection and drawing helpers in world coordinates
/// with line widths and handle sizes in screen pixels.</summary>
public sealed class GizmoContext
{
    private static readonly Dictionary<(Color, double, bool), IPen> Pens = [];
    private static readonly Dictionary<Color, IBrush> Brushes = [];
    private static readonly Typeface LabelFace = new("Inter", FontStyle.Normal, FontWeight.SemiBold);
    private const double CullMargin = 16;
    private readonly HashSet<Entity> _selected;
    private readonly Entity _hovered;
    private readonly bool _cull;

    internal GizmoContext(DrawingContext drawing, ViewportCamera camera, World world, IEditWorld editWorld, HashSet<Entity> selected, Entity hovered)
    {
        Drawing = drawing;
        Camera = camera;
        World = world;
        EditWorld = editWorld;
        _selected = selected;
        _hovered = hovered;
        Palette = GizmoPalette.Current;
        _cull = camera.ViewSize is { Width: > 0, Height: > 0 };
        View = new Rect(camera.ViewSize).Inflate(CullMargin);
    }

    public DrawingContext Drawing { get; }

    public ViewportCamera Camera { get; }

    /// <summary>The edit world, which mirrors the open scene.</summary>
    public World World { get; }

    public IEditWorld EditWorld { get; }

    public GizmoPalette Palette { get; }

    /// <summary>The screen area worth drawing in: the view and a margin for strokes and handles.</summary>
    public Rect View { get; }

    /// <summary>Whether a screen area between two corners in any order reaches into <see cref="View"/>.</summary>
    public bool IsVisible(Point a, Point b) =>
        !_cull || Math.Max(a.X, b.X) >= View.X && Math.Min(a.X, b.X) <= View.Right && Math.Max(a.Y, b.Y) >= View.Y && Math.Min(a.Y, b.Y) <= View.Bottom;

    /// <summary>World units per screen pixel.</summary>
    public float PixelSize => 1 / Math.Max(Camera.Zoom, 1e-4f);

    /// <summary>The opacity to draw an unselected entity's gizmo with, faint but visible.</summary>
    public const double Faint = 0.32;

    public bool IsSelected(Entity entity) => _selected.Contains(entity);

    /// <summary>Whether the pointer is over the entity in the viewport.</summary>
    public bool IsHovered(Entity entity) => entity == _hovered;

    /// <summary>Whether any entity is selected, so providers can skip work.</summary>
    public bool HasSelection => _selected.Count > 0;

    public Point ToScreen(Vector2 world) => Camera.WorldToScreen(world);

    public void Line(Vector2 from, Vector2 to, Color color, double thickness = 1.5, double opacity = 1, bool dashed = false)
    {
        var a = ToScreen(from);
        var b = ToScreen(to);
        if (IsVisible(a, b))
            Drawing.DrawLine(Pen(color, thickness, dashed, opacity), a, b);
    }

    /// <summary>A polyline or polygon through world points.</summary>
    public void Polyline(ReadOnlySpan<Vector2> points, Color color, bool closed, double thickness = 1.5, double opacity = 1, bool dashed = false, double fillOpacity = 0)
    {
        if (points.Length < 2)
            return;
        var min = ToScreen(points[0]);
        var max = min;
        for (var i = 1; i < points.Length; i++)
        {
            var point = ToScreen(points[i]);
            min = new Point(Math.Min(min.X, point.X), Math.Min(min.Y, point.Y));
            max = new Point(Math.Max(max.X, point.X), Math.Max(max.Y, point.Y));
        }

        if (!IsVisible(min, max))
            return;
        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            stream.BeginFigure(ToScreen(points[0]), closed && fillOpacity > 0);
            for (var i = 1; i < points.Length; i++)
                stream.LineTo(ToScreen(points[i]));
            stream.EndFigure(closed);
        }

        Drawing.DrawGeometry(fillOpacity > 0 ? Fill(color, fillOpacity * opacity) : null, Pen(color, thickness, dashed, opacity), geometry);
    }

    public void Circle(Vector2 center, float radius, Color color, double thickness = 1.5, double opacity = 1, bool dashed = false, double fillOpacity = 0)
    {
        var screen = ToScreen(center);
        var r = radius * Camera.Zoom;
        if (!IsVisible(new Point(screen.X - r, screen.Y - r), new Point(screen.X + r, screen.Y + r)))
            return;
        Drawing.DrawEllipse(fillOpacity > 0 ? Fill(color, fillOpacity * opacity) : null, Pen(color, thickness, dashed, opacity), screen, r, r);
    }

    /// <summary>An arc from <paramref name="start"/> through <paramref name="sweep"/> radians, clockwise in world space.</summary>
    public void Arc(Vector2 center, float radius, float start, float sweep, Color color, double thickness = 1.5, double opacity = 1, bool dashed = false)
    {
        var segments = Math.Max(6, (int)(Math.Abs(sweep) / MathF.Tau * 64));
        Span<Vector2> points = stackalloc Vector2[segments + 1];
        for (var i = 0; i <= segments; i++)
        {
            var angle = start + sweep * i / segments;
            points[i] = center + radius * new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        }

        Polyline(points, color, closed: false, thickness, opacity, dashed);
    }

    public void Rectangle(Rect2 rect, Color color, double thickness = 1.5, double opacity = 1, bool dashed = false, double fillOpacity = 0)
    {
        Span<Vector2> corners = [new(rect.X, rect.Y), new(rect.X + rect.Width, rect.Y), new(rect.X + rect.Width, rect.Y + rect.Height), new(rect.X, rect.Y + rect.Height)];
        Polyline(corners, color, closed: true, thickness, opacity, dashed, fillOpacity);
    }

    /// <summary>A dot of constant screen size.</summary>
    public void Dot(Vector2 world, Color color, double radius = 3, double opacity = 1)
    {
        var at = ToScreen(world);
        if (IsVisible(at, at))
            Drawing.DrawEllipse(Fill(color, opacity), null, at, radius, radius);
    }

    /// <summary>A small text label above a world point, such as a camera's name.</summary>
    public void Label(Vector2 world, string text, Color color, double opacity = 1)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelFace, 10.5, Fill(color, opacity));
        var at = ToScreen(world);
        Drawing.DrawText(formatted, new Point(Math.Round(at.X), Math.Round(at.Y - formatted.Height - 3)));
    }

    /// <summary>A handle drawn the way the viewport draws every handle.</summary>
    public void Handle(Vector2 world, Color color, GizmoHandleShape shape, bool highlighted, double size = 9) =>
        DrawHandle(Drawing, ToScreen(world), highlighted ? Palette.Highlight : color, shape, size, Palette);

    internal static void DrawHandle(DrawingContext drawing, Point at, Color color, GizmoHandleShape shape, double size, GizmoPalette palette)
    {
        var half = size / 2;
        var fill = Fill(color, 1);
        var outline = Pen(palette.HandleOutline, 1.5, false, 1);
        switch (shape)
        {
            case GizmoHandleShape.Circle:
                drawing.DrawEllipse(fill, outline, at, half, half);
                break;
            case GizmoHandleShape.Diamond:
            {
                var geometry = new StreamGeometry();
                using (var stream = geometry.Open())
                {
                    stream.BeginFigure(new Point(at.X, at.Y - half - 1), true);
                    stream.LineTo(new Point(at.X + half + 1, at.Y));
                    stream.LineTo(new Point(at.X, at.Y + half + 1));
                    stream.LineTo(new Point(at.X - half - 1, at.Y));
                    stream.EndFigure(true);
                }

                drawing.DrawGeometry(fill, outline, geometry);
                break;
            }
            default:
                drawing.DrawRectangle(fill, outline, new Rect(Math.Round(at.X - half) + 0.5, Math.Round(at.Y - half) + 0.5, size - 1, size - 1), 2, 2);
                break;
        }
    }

    internal static IPen Pen(Color color, double thickness, bool dashed, double opacity)
    {
        var faded = opacity >= 1 ? color : Color.FromArgb((byte)Math.Round(color.A * Math.Clamp(opacity, 0, 1)), color.R, color.G, color.B);
        var key = (faded, thickness, dashed);
        if (!Pens.TryGetValue(key, out var pen))
        {
            Pens[key] = pen = new ImmutablePen(new ImmutableSolidColorBrush(faded), thickness,
                dashed ? new ImmutableDashStyle([4, 3], 0) : null, PenLineCap.Round, PenLineJoin.Round);
        }

        return pen;
    }

    internal static IBrush Fill(Color color, double opacity)
    {
        var faded = opacity >= 1 ? color : Color.FromArgb((byte)Math.Round(color.A * Math.Clamp(opacity, 0, 1)), color.R, color.G, color.B);
        if (!Brushes.TryGetValue(faded, out var brush))
            Brushes[faded] = brush = new ImmutableSolidColorBrush(faded);
        return brush;
    }
}
