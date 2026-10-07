using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.VisualTree;
using Talesmith.Assets.Maps;
using Talesmith.Editor.TileMaps.Rendering;
using Talesmith.Editor.Viewport.Tools;
using Talesmith.Grids;

namespace Talesmith.Editor.TileMaps.Controls;

/// <summary>Tiles picked in the palette: one tile, tiles toggled with Ctrl, or a rectangle of the atlas for a stamp.</summary>
/// <param name="columns">For rectangles, the rectangle's width in tiles; its rows follow from the ids.</param>
public sealed class TilesPickedEventArgs(IReadOnlyList<int> ids, bool isToggle, bool isRectangle, int columns) : EventArgs
{
    public IReadOnlyList<int> Ids { get; } = ids;

    public bool IsToggle { get; } = isToggle;

    public bool IsRectangle { get; } = isRectangle;

    public int Columns { get; } = columns;
}

/// <summary>The tiles of a tileset as a fast, virtualized grid: image tilesets keep their atlas layout so a dragged rectangle becomes a stamp, color
/// tilesets show cell-shaped swatches, and a filter shows the matching tiles only.</summary>
/// <remarks>Click picks a tile, Ctrl-click toggles tiles for the random brush, dragging picks a rectangle and double-click edits a tile. Only the
/// visible rows are drawn, so tilesets with thousands of tiles stay smooth.</remarks>
public sealed class TilePalette : Control
{
    public static readonly StyledProperty<Tileset?> TilesetProperty = AvaloniaProperty.Register<TilePalette, Tileset?>(nameof(Tileset));

    public static readonly StyledProperty<double> TileSizeProperty = AvaloniaProperty.Register<TilePalette, double>(nameof(TileSize), 44);

    public static readonly StyledProperty<string?> FilterProperty = AvaloniaProperty.Register<TilePalette, string?>(nameof(Filter));

    public static readonly StyledProperty<IReadOnlyCollection<int>?> SelectedIdsProperty =
        AvaloniaProperty.Register<TilePalette, IReadOnlyCollection<int>?>(nameof(SelectedIds));

    /// <summary>Increases when the tileset's tiles changed, so the palette redraws.</summary>
    public static readonly StyledProperty<long> RevisionProperty = AvaloniaProperty.Register<TilePalette, long>(nameof(Revision));

    private const double Gap = 2;
    private const double Padding = 6;

    private readonly List<int> _visibleIds = [];
    private int _columns;
    private bool _atlasLayout;
    private int? _hovered;
    private (int Column, int Row)? _pressCell;
    private (int Column, int Row)? _dragCell;
    private bool _toggle;
    private ScrollViewer? _scroller;

    static TilePalette()
    {
        AffectsMeasure<TilePalette>(TilesetProperty, TileSizeProperty, FilterProperty, RevisionProperty);
        AffectsRender<TilePalette>(SelectedIdsProperty, CellSwatch.KindProperty);
        FocusableProperty.OverrideDefaultValue<TilePalette>(true);
    }

    public Tileset? Tileset
    {
        get => GetValue(TilesetProperty);
        set => SetValue(TilesetProperty, value);
    }

    /// <summary>The size of a palette cell in pixels.</summary>
    public double TileSize
    {
        get => GetValue(TileSizeProperty);
        set => SetValue(TileSizeProperty, value);
    }

    /// <summary>Words that tiles must match by name, id, or property name or value.</summary>
    public string? Filter
    {
        get => GetValue(FilterProperty);
        set => SetValue(FilterProperty, value);
    }

    public IReadOnlyCollection<int>? SelectedIds
    {
        get => GetValue(SelectedIdsProperty);
        set => SetValue(SelectedIdsProperty, value);
    }

    public long Revision
    {
        get => GetValue(RevisionProperty);
        set => SetValue(RevisionProperty, value);
    }

    /// <summary>Raised when tiles were picked.</summary>
    public event EventHandler<TilesPickedEventArgs>? TilesPicked;

    /// <summary>Raised with a tile's id when it was double-clicked, to edit it.</summary>
    public event EventHandler<int>? TileActivated;

    /// <summary>Whether a tile matches filter words by name, id, or property name or value.</summary>
    public static bool Matches(Tileset tileset, int id, IReadOnlyList<string> words)
    {
        if (words.Count == 0)
            return true;
        var info = tileset.Find(id);
        foreach (var word in words)
        {
            var matched = id.ToString(System.Globalization.CultureInfo.InvariantCulture) == word.TrimStart('#')
                          || (info?.Name?.Contains(word, StringComparison.OrdinalIgnoreCase) ?? false)
                          || $"Tile {id}".Contains(word, StringComparison.OrdinalIgnoreCase)
                          || (info?.Properties.Values.Any(p => p.Key.Contains(word, StringComparison.OrdinalIgnoreCase)
                                                               || p.Value.Raw.Contains(word, StringComparison.OrdinalIgnoreCase)) ?? false);
            if (!matched)
                return false;
        }

        return true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _scroller = this.FindAncestorOfType<ScrollViewer>();
        if (_scroller is not null)
            _scroller.ScrollChanged += OnScrollChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_scroller is not null)
            _scroller.ScrollChanged -= OnScrollChanged;
        _scroller = null;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _visibleIds.Clear();
        if (Tileset is not { } tileset)
            return default;
        var words = (Filter ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _atlasLayout = !tileset.IsColorTileset && words.Length == 0 && tileset.Columns > 0;
        for (var id = 0; id < tileset.TileCount; id++)
        {
            if (Matches(tileset, id, words))
                _visibleIds.Add(id);
        }

        var cell = TileSize + Gap;
        var width = double.IsInfinity(availableSize.Width) ? 320 : availableSize.Width;
        _columns = _atlasLayout ? tileset.Columns : Math.Max(1, (int)((width - Padding * 2 + Gap) / cell));
        var rows = (_visibleIds.Count + _columns - 1) / _columns;
        return new Size(Math.Max(width, _columns * cell - Gap + Padding * 2), rows * cell - Gap + Padding * 2);
    }

    public override void Render(DrawingContext context)
    {
        if (Tileset is not { } tileset || _visibleIds.Count == 0)
            return;
        var atlas = TileArt.Atlas(tileset);
        var cell = TileSize + Gap;
        var visible = VisibleArea();
        var firstRow = Math.Max(0, (int)((visible.Top - Padding) / cell));
        var lastRow = (int)((visible.Bottom - Padding) / cell);
        var accent = SelectTool.AccentColor();
        var selectedFill = new ImmutableSolidColorBrush(accent, 0.18);
        var selectedPen = new ImmutablePen(new ImmutableSolidColorBrush(accent), 1.75);
        var hoverFill = Resource("SurfaceHoverBrush");
        var kind = CellSwatch.GetKind(this);
        var selected = SelectedIds;
        var dragRect = DragRectangle();
        using var options = context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.MediumQuality });
        for (var row = firstRow; row <= lastRow; row++)
        {
            for (var column = 0; column < _columns; column++)
            {
                var index = row * _columns + column;
                if (index >= _visibleIds.Count)
                    return;
                var id = _visibleIds[index];
                var rect = new Rect(Padding + column * cell, Padding + row * cell, TileSize, TileSize);
                var isSelected = selected?.Contains(id) == true || (dragRect is { } r && column >= r.Left && column <= r.Right && row >= r.Top && row <= r.Bottom);
                if (isSelected)
                    context.DrawRectangle(selectedFill, null, rect, 5, 5);
                else if (id == _hovered)
                    context.DrawRectangle(hoverFill, null, rect, 5, 5);
                DrawTile(context, tileset, atlas, id, rect.Deflate(3), kind);
                if (isSelected)
                    context.DrawRectangle(null, selectedPen, rect.Deflate(0.5), 5, 5);
                if (tileset.Find(id)?.IsAnimated == true)
                    context.DrawEllipse(new ImmutableSolidColorBrush(accent), null, new Point(rect.Right - 6, rect.Top + 6), 3, 3);
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        if (_pressCell is not null && CellAt(position) is { } cell)
        {
            if (_dragCell != cell)
            {
                _dragCell = cell;
                InvalidateVisual();
            }

            return;
        }

        var hovered = IdAt(position);
        if (hovered == _hovered)
            return;
        _hovered = hovered;
        ToolTip.SetTip(this, hovered is { } id && Tileset is { } tileset ? Describe(tileset, id) : null);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hovered = null;
        ToolTip.SetTip(this, null);
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed || CellAt(point.Position) is not { } cell || IdAt(point.Position) is not { } id)
            return;
        if (e.ClickCount >= 2)
        {
            TileActivated?.Invoke(this, id);
            e.Handled = true;
            return;
        }

        _toggle = (e.KeyModifiers & KeyModifiers.Control) != 0;
        _pressCell = _dragCell = cell;
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_pressCell is not { } start)
            return;
        var end = _dragCell ?? start;
        _pressCell = _dragCell = null;
        e.Pointer.Capture(null);
        if (_toggle || start == end || !_atlasLayout)
        {
            if (IdAtCell(start.Column, start.Row) is { } id)
                TilesPicked?.Invoke(this, new TilesPickedEventArgs([id], _toggle, false, 1));
        }
        else
        {
            var (left, top, right, bottom) = (Math.Min(start.Column, end.Column), Math.Min(start.Row, end.Row), Math.Max(start.Column, end.Column),
                Math.Max(start.Row, end.Row));
            var ids = new List<int>();
            for (var row = top; row <= bottom; row++)
            {
                for (var column = left; column <= right; column++)
                    ids.Add(IdAtCell(column, row) ?? -1);
            }

            TilesPicked?.Invoke(this, new TilesPickedEventArgs(ids, false, true, right - left + 1));
        }

        InvalidateVisual();
    }

    /// <summary>Scrolls a tile into view, such as after it was picked from the map.</summary>
    public void ScrollToTile(int id)
    {
        var index = _visibleIds.IndexOf(id);
        if (index < 0 || _columns == 0)
            return;
        var cell = TileSize + Gap;
        var top = Padding + index / _columns * cell;
        if (_scroller is { } scroller && (top < scroller.Offset.Y || top + TileSize > scroller.Offset.Y + scroller.Viewport.Height))
            scroller.Offset = scroller.Offset.WithY(Math.Max(0, top - (scroller.Viewport.Height - TileSize) / 2));
    }

    private static string Describe(Tileset tileset, int id)
    {
        var info = tileset.Find(id);
        var name = info?.Name is { Length: > 0 } n ? n : $"Tile {id}";
        var details = $"{name} · #{id}";
        if (info?.IsAnimated == true)
            details += $" · animated, {info.Animation.Count} frames";
        if (info?.Collision.Count > 0)
            details += " · collision shapes";
        if (info?.Properties.Count > 0)
            details += Environment.NewLine + string.Join(Environment.NewLine, info.Properties.Values.Select(p => $"{p.Key}: {p.Value.Raw}"));
        return details;
    }

    private static void DrawTile(DrawingContext context, Tileset tileset, global::Avalonia.Media.Imaging.Bitmap? atlas, int id, Rect area, GridKind kind)
    {
        if (atlas is not null)
        {
            var source = TileArt.SourceRect(tileset, id, atlas);
            if (source.Width <= 0 || source.Height <= 0)
                return;
            var scale = Math.Min(area.Width / source.Width, area.Height / source.Height);
            var size = new Size(source.Width * scale, source.Height * scale);
            var destination = new Rect(area.Center.X - size.Width / 2, area.Center.Y - size.Height / 2, size.Width, size.Height);
            context.DrawImage(atlas, new Rect(source.X, source.Y, source.Width, source.Height), destination);
            return;
        }

        if (CellSwatch.Shape(kind, area) is { } shape)
            context.DrawGeometry(TileArt.ColorBrush(tileset, id), null, shape);
    }

    private (int Left, int Top, int Right, int Bottom)? DragRectangle()
    {
        if (_pressCell is not { } start || _dragCell is not { } end || _toggle || !_atlasLayout)
            return null;
        return (Math.Min(start.Column, end.Column), Math.Min(start.Row, end.Row), Math.Max(start.Column, end.Column), Math.Max(start.Row, end.Row));
    }

    private (int Column, int Row)? CellAt(Point position)
    {
        if (_columns == 0)
            return null;
        var cell = TileSize + Gap;
        var column = (int)Math.Floor((position.X - Padding) / cell);
        var row = (int)Math.Floor((position.Y - Padding) / cell);
        var rows = (_visibleIds.Count + _columns - 1) / _columns;
        return (Math.Clamp(column, 0, _columns - 1), Math.Clamp(row, 0, Math.Max(0, rows - 1)));
    }

    private int? IdAt(Point position)
    {
        var cell = TileSize + Gap;
        var column = (int)Math.Floor((position.X - Padding) / cell);
        var row = (int)Math.Floor((position.Y - Padding) / cell);
        if (column < 0 || row < 0 || column >= _columns || (position.X - Padding) % cell > TileSize || (position.Y - Padding) % cell > TileSize)
            return null;
        return IdAtCell(column, row);
    }

    private int? IdAtCell(int column, int row)
    {
        var index = row * _columns + column;
        return column >= 0 && column < _columns && index >= 0 && index < _visibleIds.Count ? _visibleIds[index] : null;
    }

    private Rect VisibleArea()
    {
        if (_scroller is null)
            return new Rect(Bounds.Size);
        var origin = this.TranslatePoint(default, _scroller) ?? default;
        return new Rect(-origin.X, -origin.Y, _scroller.Viewport.Width, _scroller.Viewport.Height).Inflate(TileSize);
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e) => InvalidateVisual();

    private IBrush Resource(string key) => this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Transparent;
}
