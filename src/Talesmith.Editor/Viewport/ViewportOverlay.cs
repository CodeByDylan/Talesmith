using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Talesmith.Assets;
using Talesmith.Editor.Documents;
using Talesmith.Editor.DragAndDrop;
using Talesmith.Editor.Hierarchy;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Selection;
using Talesmith.Editor.Viewport.Gizmos;
using Talesmith.Editor.Viewport.Tools;

namespace Talesmith.Editor.Viewport;

/// <summary>The editor layer above the game view: draws the grid, entity icons, selection and hover outlines and the active tool, and
/// turns pointer and key input into navigation and tool input.</summary>
public sealed class ViewportOverlay : Control
{
    private const double MinGridSpacing = 9;

    private readonly ViewportService _viewport;
    private readonly ToolManager _tools;
    private readonly ISelectionService _selection;
    private readonly ISceneDocumentService _documents;
    private readonly GridCache _grid = new();
    private readonly IconCache _icons = new();
    private readonly GizmoLayer? _gizmos;
    private readonly SceneAssetDrop? _assetDrop;
    private readonly IAssetCatalog? _catalog;
    private Vector2? _dropPoint;
    private Point? _panFrom;
    private bool _spaceHeld;
    private bool _animationRequested;
    private TimeSpan? _lastAnimationFrame;

    public ViewportOverlay(ViewportService viewport, ToolManager tools, ISelectionService selection, ISceneDocumentService documents)
    {
        _viewport = viewport;
        _tools = tools;
        _selection = selection;
        _documents = documents;
        Focusable = true;
        ClipToBounds = true;
        Background = Brushes.Transparent;
        _gizmos = tools.Context.Services.GetService(typeof(GizmoLayer)) as GizmoLayer;
        _gizmos?.InvalidateRequested += (_, _) =>
        {
            UpdateCursor();
            InvalidateVisual();
        };
        _assetDrop = tools.Context.Services.GetService(typeof(SceneAssetDrop)) as SceneAssetDrop;
        _catalog = (tools.Context.Services.GetService(typeof(IProjectService)) as IProjectService)?.Catalog;
        if (_assetDrop is not null && _catalog is not null)
        {
            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DragOverEvent, OnDragOver);
            AddHandler(DragDrop.DragLeaveEvent, (_, _) => SetDropPoint(null));
            AddHandler(DragDrop.DropEvent, OnDrop);
        }

        _tools.Context.Attach(this);
        _tools.Context.InvalidateRequested += (_, _) => InvalidateVisual();
        _viewport.Invalidated += (_, _) => Invalidate();
        _tools.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ToolManager.ActiveTool))
                UpdateCursor();
        };
        UpdateCursor();
    }

    public IBrush? Background { get; set; }

    private ViewportCamera Camera => _viewport.Camera;

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (Background is { } background)
            context.FillRectangle(background, bounds);

        var palette = Palette.Current(IsDarkScene());
        if (_tools.ActiveTool is IViewportGridTool { HasGrid: true } gridTool)
        {
            if (_viewport.Options.ShowTileGrid)
                gridTool.RenderGrid(_tools.Context, context, palette.Line);
        }
        else if (_viewport.Options.ShowGrid)
        {
            DrawGrid(context, palette);
        }

        var visuals = _viewport.Picker.Collect(Camera.Zoom);
        var hovered = _tools.Context.Hovered;
        var visible = Camera.VisibleWorld;
        foreach (var visual in visuals)
        {
            if (visual.Kind == EntityVisualKind.Icon || !visible.Intersects(visual.Bounds))
                continue;
            if (_selection.IsSelected(visual.DocumentId))
                DrawOutline(context, visual, palette.SelectionPen, palette.SelectionFill);
            else if (visual.DocumentId == hovered)
                DrawOutline(context, visual, palette.HoverPen, null);
        }

        if (_viewport.Options.ShowIcons)
        {
            _icons.Prepare(palette, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
            foreach (var visual in visuals)
            {
                if (visual.Kind == EntityVisualKind.Icon && visible.Intersects(visual.Bounds))
                    DrawIcon(context, visual, _selection.IsSelected(visual.DocumentId), visual.DocumentId == hovered);
            }
        }

        _gizmos?.Render(_tools.Context, context);
        _tools.Render(context);
        if (_dropPoint is { } drop)
            DrawDropPoint(context, Camera.WorldToScreen(drop), palette);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Camera.SetViewSize(finalSize);
        _viewport.OnViewSized();
        return base.ArrangeOverride(finalSize);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus(NavigationMethod.Pointer);
        var point = e.GetCurrentPoint(this);
        var properties = point.Properties;
        if (properties.IsMiddleButtonPressed || properties.IsRightButtonPressed || (_spaceHeld && properties.IsLeftButtonPressed))
        {
            _panFrom = point.Position;
            e.Pointer.Capture(this);
            Cursor = new Cursor(StandardCursorType.SizeAll);
            e.Handled = true;
            return;
        }

        var args = Args(e, point, e.ClickCount);
        if (_gizmos?.PointerPressed(_tools.Context, args) != true)
            _tools.PointerPressed(args);
        _viewport.Wake();
        UpdateCursor();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetCurrentPoint(this);
        PointerWorld = Camera.ScreenToWorld(point.Position);
        PointerMovedOverView?.Invoke(this, EventArgs.Empty);
        if (_panFrom is { } from)
        {
            Camera.PanBy(point.Position - from);
            _panFrom = point.Position;
            e.Handled = true;
            return;
        }

        var args = Args(e, point, 0);
        if (_gizmos?.PointerMoved(_tools.Context, args) != true)
            _tools.PointerMoved(args);
        UpdateCursor();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_panFrom is not null)
        {
            _panFrom = null;
            e.Pointer.Capture(null);
            UpdateCursor();
            e.Handled = true;
            return;
        }

        var args = Args(e, e.GetCurrentPoint(this), 0);
        if (_gizmos?.PointerReleased(_tools.Context, args) != true)
            _tools.PointerReleased(args);
        UpdateCursor();
        e.Handled = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        PointerWorld = null;
        PointerMovedOverView?.Invoke(this, EventArgs.Empty);
        _tools.PointerExited();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_panFrom is null)
            return;
        _panFrom = null;
        UpdateCursor();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var position = e.GetPosition(this);
        if ((e.KeyModifiers & KeyModifiers.Shift) != 0)
        {
            Camera.PanBy(new global::Avalonia.Vector(e.Delta.Y * 48, 0));
        }
        else if (Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y))
        {
            Camera.PanBy(new global::Avalonia.Vector(e.Delta.X * 48, 0));
        }
        else
        {
            var step = (float)Math.Clamp(_viewport.ZoomStep, 1.02, 3);
            Camera.ZoomAt(position, MathF.Pow(step, (float)e.Delta.Y), _viewport.SmoothZoom);
            RequestAnimation();
        }

        e.Handled = true;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.None;
        if (_documents.Active is null || !EditorDragData.TryGet(e, _catalog!, out var data) || !data.Assets.Any(_assetDrop!.CanCreate))
        {
            SetDropPoint(null);
            return;
        }

        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
        SetDropPoint(_viewport.Options.Snap(Camera.ScreenToWorld(e.GetPosition(this))));
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        SetDropPoint(null);
        if (_documents.Active is null || !EditorDragData.TryGet(e, _catalog!, out var data))
            return;
        var at = _viewport.Options.Snap(Camera.ScreenToWorld(e.GetPosition(this)));
        if (_assetDrop!.Drop([.. data.Assets.Where(_assetDrop.CanCreate)], null, -1, at).Count == 0)
            return;
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
        Focus();
    }

    private void SetDropPoint(Vector2? point)
    {
        if (_dropPoint == point)
            return;
        _dropPoint = point;
        InvalidateVisual();
    }

    private static void DrawDropPoint(DrawingContext context, Point at, Palette palette)
    {
        context.DrawEllipse(palette.SelectionFill, palette.SelectionPen, at, 14, 14);
        context.DrawLine(palette.SelectionPen, at + new global::Avalonia.Vector(-6, 0), at + new global::Avalonia.Vector(6, 0));
        context.DrawLine(palette.SelectionPen, at + new global::Avalonia.Vector(0, -6), at + new global::Avalonia.Vector(0, 6));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
            return;
        if (e.Key == Key.Escape && _gizmos?.Cancel() == true)
        {
            e.Handled = true;
            UpdateCursor();
            return;
        }

        if (_tools.KeyDown(e))
        {
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.None)
        {
            _spaceHeld = true;
            UpdateCursor();
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        _tools.KeyUp(e);
        if (e.Key == Key.Space)
        {
            _spaceHeld = false;
            UpdateCursor();
        }
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        _spaceHeld = false;
        UpdateCursor();
    }

    /// <summary>The world position under the pointer, or null when the pointer is outside the viewport.</summary>
    public Vector2? PointerWorld { get; private set; }

    /// <summary>Raised when <see cref="PointerWorld"/> changed.</summary>
    public event EventHandler? PointerMovedOverView;

    private void Invalidate()
    {
        InvalidateVisual();
        if (Camera.IsAnimating)
            RequestAnimation();
    }

    private void RequestAnimation()
    {
        if (_animationRequested || TopLevel.GetTopLevel(this) is not { } top)
            return;
        _animationRequested = true;
        top.RequestAnimationFrame(OnAnimationFrame);
    }

    private void OnAnimationFrame(TimeSpan time)
    {
        _animationRequested = false;
        var elapsed = _lastAnimationFrame is { } last ? time - last : TimeSpan.FromMilliseconds(16);
        _lastAnimationFrame = time;
        if (elapsed > TimeSpan.FromMilliseconds(100))
            elapsed = TimeSpan.FromMilliseconds(16);
        if (Camera.Advance(elapsed) && Camera.IsAnimating)
            RequestAnimation();
        else
            _lastAnimationFrame = null;
    }

    private ViewportPointerEventArgs Args(PointerEventArgs e, PointerPoint point, int clickCount) =>
        new(e, point.Position, Camera.ScreenToWorld(point.Position), point.Properties, clickCount);

    private void UpdateCursor() =>
        Cursor = _panFrom is not null ? new Cursor(StandardCursorType.SizeAll) : _spaceHeld ? new Cursor(StandardCursorType.Hand)
            : _gizmos?.Cursor ?? _tools.ActiveTool.Cursor ?? Cursor.Default;

    private bool IsDarkScene()
    {
        var clear = _documents.Active?.Document.Environment.ClearColor ?? _viewport.ClearColor;
        return (0.299 * clear.R + 0.587 * clear.G + 0.114 * clear.B) / 255 < 0.6;
    }

    private void DrawGrid(DrawingContext context, Palette palette)
    {
        var size = Bounds.Size;
        var gridSize = _viewport.Options.GridSize;
        if (gridSize <= 0 || size.Width <= 0)
            return;
        var (minor, major) = _grid.Get(Camera, gridSize, size);
        if (minor is not null)
            context.DrawGeometry(null, palette.Minor(_grid.MinorOpacity), minor);
        if (major is not null)
            context.DrawGeometry(null, palette.MajorPen, major);

        var origin = Camera.WorldToScreen(Vector2.Zero);
        if (origin.Y >= 0 && origin.Y <= size.Height)
            context.DrawLine(palette.AxisXPen, new Point(0, Snap(origin.Y)), new Point(size.Width, Snap(origin.Y)));
        if (origin.X >= 0 && origin.X <= size.Width)
            context.DrawLine(palette.AxisYPen, new Point(Snap(origin.X), 0), new Point(Snap(origin.X), size.Height));
    }

    private void DrawOutline(DrawingContext context, EntityVisual visual, IPen pen, IBrush? fill)
    {
        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            stream.BeginFigure(Camera.WorldToScreen(visual.Corners[0]), true);
            for (var i = 1; i < 4; i++)
                stream.LineTo(Camera.WorldToScreen(visual.Corners[i]));
            stream.EndFigure(true);
        }

        context.DrawGeometry(fill, pen, geometry);
    }

    private void DrawIcon(DrawingContext context, EntityVisual visual, bool selected, bool hovered)
    {
        var center = Camera.WorldToScreen(visual.Position);
        var scaling = _icons.Scaling;
        const double half = IconCache.Extent / 2;
        var left = Math.Round((center.X - half) * scaling) / scaling;
        var top = Math.Round((center.Y - half) * scaling) / scaling;
        var image = _icons.Get(visual.Icon, selected, hovered);
        // The source rectangle is in pixels; the default, the size in device-independent units, would crop the image above scale 1.
        context.DrawImage(image, new Rect(image.PixelSize.ToSize(1)), new Rect(left, top, IconCache.Extent, IconCache.Extent));
    }

    /// <summary>Entity icon badges drawn once per icon, color and state, so the viewport draws each icon as one image.</summary>
    private sealed class IconCache
    {
        public const double Extent = EntityPicker.IconSize + 6;

        private readonly Dictionary<(Geometry? Icon, Color? Color, bool Selected, bool Hovered), RenderTargetBitmap> _images = [];
        private Palette? _palette;

        public double Scaling { get; private set; } = 1;

        public void Prepare(Palette palette, double scaling)
        {
            if (ReferenceEquals(palette, _palette) && scaling == Scaling)
                return;
            foreach (var image in _images.Values)
                image.Dispose();
            _images.Clear();
            _palette = palette;
            Scaling = scaling;
        }

        public RenderTargetBitmap Get(EntityIcon? icon, bool selected, bool hovered)
        {
            var key = (icon?.Icon, icon?.Color, selected, hovered);
            if (!_images.TryGetValue(key, out var image))
                _images[key] = image = Draw(_palette!, icon, selected, hovered);
            return image;
        }

        private RenderTargetBitmap Draw(Palette palette, EntityIcon? entityIcon, bool selected, bool hovered)
        {
            var pixels = (int)Math.Ceiling(Extent * Scaling);
            var image = new RenderTargetBitmap(new PixelSize(pixels, pixels), new global::Avalonia.Vector(96 * Scaling, 96 * Scaling));
            using var context = image.CreateDrawingContext();
            const double size = EntityPicker.IconSize;
            const double inset = (Extent - size) / 2;
            var rect = new Rect(inset, inset, size, size);
            using var opacity = context.PushOpacity(selected || hovered ? 1 : 0.8);
            var background = selected ? palette.AccentBrush : palette.BadgeBrush;
            var border = selected || hovered ? palette.SelectionPen : palette.BadgeBorderPen;
            context.DrawRectangle(palette.ShadowBrush, null, rect.Translate(new global::Avalonia.Vector(0, 1.5)), 8, 8);
            context.DrawRectangle(background, border, rect, 8, 8);
            if (entityIcon is not { Icon: { } icon } value)
                return image;
            var iconColor = selected ? Colors.White : value.Color ?? palette.IconColor;
            var pen = new ImmutablePen(new ImmutableSolidColorBrush(iconColor), 2.2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            const double iconSize = 15;
            const double scale = iconSize / 24;
            using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(Extent / 2 - iconSize / 2, Extent / 2 - iconSize / 2)))
                context.DrawGeometry(null, pen, icon);
            return image;
        }
    }

    private static double Snap(double value) => Math.Round(value) + 0.5;

    /// <summary>The grid's lines for the current view, rebuilt only when the camera, size or grid spacing changes.</summary>
    private sealed class GridCache
    {
        private (Vector2 Position, float Zoom, Size Size, double Grid) _key;
        private Geometry? _minor;
        private Geometry? _major;

        public double MinorOpacity { get; private set; }

        public (Geometry? Minor, Geometry? Major) Get(ViewportCamera camera, double gridSize, Size size)
        {
            var key = (camera.Position, camera.Zoom, size, gridSize);
            if (key == _key)
                return (_minor, _major);
            _key = key;

            var step = gridSize;
            while (step * camera.Zoom < MinGridSpacing)
                step *= 2;
            var spacing = step * camera.Zoom;
            MinorOpacity = Math.Clamp((spacing - MinGridSpacing) / 18, 0, 1);
            var majorStep = step * 4;
            var visible = camera.VisibleWorld;
            _minor = Lines(camera, step, majorStep, visible, size, major: false);
            _major = Lines(camera, majorStep, 0, visible, size, major: true);
            return (_minor, _major);
        }

        private static StreamGeometry? Lines(ViewportCamera camera, double step, double skip, Mathematics.Rect2 visible, Size size, bool major)
        {
            var geometry = new StreamGeometry();
            var any = false;
            using (var stream = geometry.Open())
            {
                for (var x = Math.Floor(visible.X / step) * step; x <= visible.X + visible.Width; x += step)
                {
                    if (!major && IsMultiple(x, skip) || x == 0)
                        continue;
                    var screen = Snap(camera.WorldToScreen(new Vector2((float)x, 0)).X);
                    stream.BeginFigure(new Point(screen, 0), false);
                    stream.LineTo(new Point(screen, size.Height));
                    stream.EndFigure(false);
                    any = true;
                }

                for (var y = Math.Floor(visible.Y / step) * step; y <= visible.Y + visible.Height; y += step)
                {
                    if (!major && IsMultiple(y, skip) || y == 0)
                        continue;
                    var screen = Snap(camera.WorldToScreen(new Vector2(0, (float)y)).Y);
                    stream.BeginFigure(new Point(0, screen), false);
                    stream.LineTo(new Point(size.Width, screen));
                    stream.EndFigure(false);
                    any = true;
                }
            }

            return any ? geometry : null;
        }

        private static bool IsMultiple(double value, double step) => step > 0 && Math.Abs(Math.IEEERemainder(value, step)) < 1e-6;
    }

    /// <summary>The colors of the overlay for the current theme and scene background.</summary>
    private sealed class Palette
    {
        private static (Color Accent, bool Dark, bool Light, Palette Value)? _cached;
        private readonly Color _line;
        private readonly Dictionary<int, IPen> _minor = [];

        private Palette(Color accent, bool darkScene, bool lightTheme)
        {
            _line = darkScene ? Colors.White : Color.FromRgb(16, 18, 24);
            var accentBrush = new ImmutableSolidColorBrush(accent);
            AccentBrush = accentBrush;
            SelectionPen = new ImmutablePen(accentBrush, 2);
            SelectionFill = new ImmutableSolidColorBrush(accent, 0.08);
            HoverPen = new ImmutablePen(new ImmutableSolidColorBrush(accent, 0.65), 1.25);
            MajorPen = new ImmutablePen(new ImmutableSolidColorBrush(_line, darkScene ? 0.16 : 0.2), 1);
            AxisXPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#E5534B"), 0.75), 1.25);
            AxisYPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#4FAE5C"), 0.75), 1.25);
            BadgeBrush = new ImmutableSolidColorBrush(lightTheme ? Color.Parse("#F8F9FB") : Color.Parse("#23252D"), 0.94);
            BadgeBorderPen = new ImmutablePen(new ImmutableSolidColorBrush(lightTheme ? Color.Parse("#C9CDD6") : Color.Parse("#3A3D48")), 1);
            ShadowBrush = new ImmutableSolidColorBrush(Colors.Black, 0.25);
            IconColor = lightTheme ? Color.Parse("#2A2D36") : Color.Parse("#E6E8EE");
        }

        public IBrush AccentBrush { get; }

        /// <summary>The color of grid lines, which stand out from the scene's background.</summary>
        public Color Line => _line;

        public IPen SelectionPen { get; }

        public IBrush SelectionFill { get; }

        public IPen HoverPen { get; }

        public IPen MajorPen { get; }

        public IPen AxisXPen { get; }

        public IPen AxisYPen { get; }

        public IBrush BadgeBrush { get; }

        public IPen BadgeBorderPen { get; }

        public IBrush ShadowBrush { get; }

        public Color IconColor { get; }

        public static Palette Current(bool darkScene)
        {
            var accent = SelectTool.AccentColor();
            var light = Application.Current?.ActualThemeVariant == global::Avalonia.Styling.ThemeVariant.Light;
            if (_cached is { } cached && cached.Accent == accent && cached.Dark == darkScene && cached.Light == light)
                return cached.Value;
            var palette = new Palette(accent, darkScene, light);
            _cached = (accent, darkScene, light, palette);
            return palette;
        }

        public IPen Minor(double opacity)
        {
            var key = (int)Math.Round(opacity * 20);
            if (!_minor.TryGetValue(key, out var pen))
                _minor[key] = pen = new ImmutablePen(new ImmutableSolidColorBrush(_line, 0.075 * key / 20.0), 1);
            return pen;
        }
    }
}
