using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Talesmith.Editor.ProjectSettings;

/// <summary>The triangle of check boxes that says which physics layers collide, with layer names along the rows and, rotated, the columns.</summary>
public sealed class CollisionMatrix : Control
{
    public static readonly StyledProperty<PhysicsSettingsPage?> PageProperty =
        AvaloniaProperty.Register<CollisionMatrix, PhysicsSettingsPage?>(nameof(Page));

    public static readonly StyledProperty<IReadOnlyList<PhysicsLayerItem>?> LayersProperty =
        AvaloniaProperty.Register<CollisionMatrix, IReadOnlyList<PhysicsLayerItem>?>(nameof(Layers));

    private const double Cell = 20;
    private const double Gap = 8;
    private (int Row, int Column)? _hover;

    static CollisionMatrix()
    {
        AffectsMeasure<CollisionMatrix>(LayersProperty);
        AffectsRender<CollisionMatrix>(PageProperty, LayersProperty);
    }

    public CollisionMatrix() => Cursor = new Cursor(StandardCursorType.Hand);

    public PhysicsSettingsPage? Page
    {
        get => GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    public IReadOnlyList<PhysicsLayerItem>? Layers
    {
        get => GetValue(LayersProperty);
        set => SetValue(LayersProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var count = Layers?.Count ?? 0;
        var label = LabelWidth();
        return new Size(label + Gap + count * Cell + 2, label + Gap + count * Cell + 2);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PageProperty)
        {
            if (change.OldValue is PhysicsSettingsPage old)
                old.PropertyChanged -= OnPageChanged;
            if (change.NewValue is PhysicsSettingsPage page)
                page.PropertyChanged += OnPageChanged;
        }
    }

    public override void Render(DrawingContext context)
    {
        if (Layers is not { Count: > 0 } layers || Page is not { } page)
            return;
        var text = Brush("TextSecondaryBrush");
        var accent = Brush("AccentBrush");
        var border = Brush("BorderStrongBrush");
        var hover = Brush("SurfaceHoverBrush");
        var label = LabelWidth();
        var origin = new Point(label + Gap, label + Gap);
        var n = layers.Count;

        if (_hover is var (hoverRow, hoverColumn))
        {
            context.FillRectangle(hover, new Rect(0, origin.Y + hoverRow * Cell, origin.X + (n - hoverRow) * Cell, Cell), 4);
            context.FillRectangle(hover, new Rect(origin.X + hoverColumn * Cell, 0, Cell, origin.Y + (n - hoverColumn) * Cell), 4);
        }

        for (var i = 0; i < n; i++)
        {
            var isHot = _hover is var (r, c) && (r == i || n - 1 - c == i);
            var name = Format(layers[i], isHot ? accent : text);
            context.DrawText(name, new Point(label - name.Width, origin.Y + i * Cell + (Cell - name.Height) / 2));

            var columnLayer = n - 1 - i;
            var column = Format(layers[columnLayer], _hover is var (hr, hc) && (hc == i || hr == columnLayer) ? accent : text);
            using (context.PushTransform(Matrix.CreateRotation(-Math.PI / 2) * Matrix.CreateTranslation(origin.X + i * Cell + (Cell - column.Height) / 2, label)))
                context.DrawText(column, new Point(0, 0));
        }

        for (var row = 0; row < n; row++)
        {
            for (var column = 0; column < n - row; column++)
            {
                var b = n - 1 - column;
                var box = new Rect(origin.X + column * Cell + 3, origin.Y + row * Cell + 3, Cell - 6, Cell - 6);
                if (page.ShouldCollide(layers[row].Index, layers[b].Index))
                {
                    context.DrawRectangle(accent, null, box, 3, 3);
                    var check = new StreamGeometry();
                    using (var g = check.Open())
                    {
                        g.BeginFigure(new Point(box.X + 3.5, box.Center.Y), false);
                        g.LineTo(new Point(box.X + box.Width * 0.43, box.Bottom - 4));
                        g.LineTo(new Point(box.Right - 3.5, box.Y + 4));
                        g.EndFigure(false);
                    }

                    context.DrawGeometry(null, new Pen(Brushes.White, 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), check);
                }
                else
                {
                    context.DrawRectangle(null, new Pen(border, 1), box.Deflate(0.5), 3, 3);
                }
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var hit = HitTest(e.GetPosition(this));
        if (hit == _hover)
            return;
        _hover = hit;
        ToolTip.SetTip(this, hit is var (row, column) && Layers is { } layers
            ? $"{LayerName(layers[row])} × {LayerName(layers[layers.Count - 1 - column])}: {(Page?.ShouldCollide(layers[row].Index, layers[layers.Count - 1 - column].Index) == true ? "collide" : "pass through each other")}"
            : null);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (HitTest(e.GetPosition(this)) is not var (row, column) || Layers is not { } layers || Page is not { } page)
            return;
        var a = layers[row].Index;
        var b = layers[layers.Count - 1 - column].Index;
        page.SetCollision(a, b, !page.ShouldCollide(a, b));
        e.Handled = true;
        InvalidateVisual();
    }

    private (int Row, int Column)? HitTest(Point point)
    {
        if (Layers is not { Count: > 0 } layers)
            return null;
        var label = LabelWidth();
        var column = (int)Math.Floor((point.X - label - Gap) / Cell);
        var row = (int)Math.Floor((point.Y - label - Gap) / Cell);
        var n = layers.Count;
        return row >= 0 && column >= 0 && row < n && column < n - row ? (row, column) : null;
    }

    private double LabelWidth() => Math.Max(60, (Layers ?? []).Select(l => Format(l, Brushes.Black).Width).DefaultIfEmpty(0).Max());

    private static FormattedText Format(PhysicsLayerItem layer, IBrush brush) =>
        new(LayerName(layer), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 11.5, brush);

    private static string LayerName(PhysicsLayerItem layer) => string.IsNullOrWhiteSpace(layer.Name) ? layer.Placeholder : layer.Name;

    private IBrush Brush(string key) => this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;

    private void OnPageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => InvalidateVisual();
}
