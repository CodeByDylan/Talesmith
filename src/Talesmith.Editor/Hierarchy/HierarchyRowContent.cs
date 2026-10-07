using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;
using Talesmith.UI;

namespace Talesmith.Editor.Hierarchy;

/// <summary>A part of a hierarchy row that does something when clicked.</summary>
public enum HierarchyRowZone
{
    None,
    Chevron,
    Visibility,
    Lock
}

/// <summary>Draws a hierarchy row's chevron, icon, name, prefab dot and visibility and lock toggles as one control.</summary>
public sealed class HierarchyRowContent : Control, ICustomHitTest
{
    private const double ChevronSize = 16;
    private const double ChevronZoneWidth = ChevronSize + 4;
    private const double IconSize = 14;
    private const double ToggleSize = 20;
    private const double RightMargin = 4;
    private const double DotSize = 6;

    private static readonly Cursor Hand = new(StandardCursorType.Hand);

    private HierarchyRow? _row;
    private HierarchyRowZone _hover;
    private FormattedText? _layout;
    private double _layoutWidth = -1;

    public HierarchyRowContent()
    {
        ClipToBounds = true;
    }

    /// <summary>Takes the pointer anywhere in the row rather than only over what it draws, so the chevron and toggles answer across their zones.</summary>
    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    /// <summary>The row whose parts are under a point, in this control's coordinates.</summary>
    public HierarchyRowZone ZoneAt(Point point)
    {
        if (_row is not { } row)
            return HierarchyRowZone.None;
        var indent = row.Depth * HierarchyRow.IndentStep;
        if (row.HasChildren && point.X >= indent && point.X < indent + ChevronZoneWidth)
            return HierarchyRowZone.Chevron;
        var right = Bounds.Width - RightMargin;
        if (row.IsEditable)
        {
            right -= ToggleSize;
            if (point.X >= right && point.X < right + ToggleSize)
                return HierarchyRowZone.Lock;
        }

        if (!row.IsMember)
        {
            right -= ToggleSize;
            if (point.X >= right && point.X < right + ToggleSize)
                return HierarchyRowZone.Visibility;
        }

        return HierarchyRowZone.None;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_row is not null)
            _row.PropertyChanged -= OnRowChanged;
        _row = DataContext as HierarchyRow;
        if (_row is not null)
            _row.PropertyChanged += OnRowChanged;
        _layout = null;
        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_row is not null)
            _row.PropertyChanged -= OnRowChanged;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_row is not null)
        {
            _row.PropertyChanged -= OnRowChanged;
            _row.PropertyChanged += OnRowChanged;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property.Name == nameof(ActualThemeVariant) || change.Property == TextElement.FontSizeProperty || change.Property == TextElement.FontFamilyProperty)
        {
            _layout = null;
            InvalidateVisual();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        SetHover(ZoneAt(e.GetPosition(this)));
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        SetHover(ZoneAt(e.GetPosition(this)));
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHover(HierarchyRowZone.None);
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, 24);

    public override void Render(DrawingContext context)
    {
        if (_row is not { } row)
            return;
        var height = Bounds.Height;
        var x = row.Depth * HierarchyRow.IndentStep;
        var dimmed = row.IsDimmed ? 0.45 : row.IsMember ? 0.8 : 1;

        if (row.HasChildren)
        {
            var hovered = _hover == HierarchyRowZone.Chevron;
            if (hovered)
                context.DrawRectangle(Brush("SurfacePressedBrush"), null, new RoundedRect(new Rect(x, (height - ToggleSize) / 2, ChevronZoneWidth, ToggleSize), 4));
            DrawIcon(context, Icons.ChevronRight, Brush(hovered ? "TextPrimaryBrush" : "TextMutedBrush"), 2.5, x + ChevronZoneWidth / 2, height / 2, 12,
                row.IsExpanded ? 90 : 0, 1);
        }

        x += 22;
        var accent = row.IsPrefabMissing ? "DangerBrush" : row.IsPrefab ? "AccentBrush" : null;
        DrawIcon(context, row.Icon, Brush(accent ?? "TextSecondaryBrush"), 2, x + IconSize / 2, height / 2, IconSize, 0, dimmed);
        x += IconSize + 7;

        var right = Bounds.Width - RightMargin;
        var showToggles = IsPointerOver;
        if (row.IsEditable)
        {
            right -= ToggleSize;
            DrawToggle(context, row.IsLocked ? Icons.Lock : Icons.Unlock, right, height, row.IsLocked, showToggles, _hover == HierarchyRowZone.Lock);
        }

        if (!row.IsMember)
        {
            right -= ToggleSize;
            DrawToggle(context, row.IsHidden ? Icons.EyeOff : Icons.Eye, right, height, row.IsHidden, showToggles, _hover == HierarchyRowZone.Visibility);
        }

        if (row.HasOverrides)
        {
            right -= DotSize + 8;
            context.DrawEllipse(Brush("AccentBrush"), null, new Point(right + 6 + DotSize / 2, height / 2), DotSize / 2, DotSize / 2);
        }

        if (row.IsRenaming)
            return;
        var width = Math.Max(0, right - x - 4);
        var layout = Layout(row, width, accent);
        using (context.PushOpacity(dimmed))
            context.DrawText(layout, new Point(x, Math.Round((height - layout.Height) / 2)));
    }

    private void DrawToggle(DrawingContext context, Geometry icon, double left, double height, bool on, bool rowHovered, bool hovered)
    {
        if (!on && !rowHovered)
            return;
        var top = (height - ToggleSize) / 2;
        if (hovered)
            context.DrawRectangle(Brush("SurfacePressedBrush"), null, new RoundedRect(new Rect(left, top, ToggleSize, ToggleSize), 4));
        var brush = Brush(on ? "WarningBrush" : hovered ? "TextPrimaryBrush" : "TextSecondaryBrush");
        DrawIcon(context, icon, brush, 2, left + ToggleSize / 2, height / 2, 13, 0, 1);
    }

    private static void DrawIcon(DrawingContext context, Geometry? icon, IBrush? brush, double stroke, double centerX, double centerY, double size, double angle,
        double opacity)
    {
        if (icon is null || brush is null)
            return;
        var scale = size / 24;
        var transform = Matrix.CreateTranslation(-12, -12) * Matrix.CreateRotation(angle * Math.PI / 180) * Matrix.CreateScale(scale, scale)
                        * Matrix.CreateTranslation(Math.Round(centerX), Math.Round(centerY));
        using (context.PushOpacity(opacity))
        using (context.PushTransform(transform))
            context.DrawGeometry(null, new Pen(brush, stroke, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), icon);
    }

    private FormattedText Layout(HierarchyRow row, double width, string? accent)
    {
        if (_layout is not null && Math.Abs(_layoutWidth - width) < 0.5)
            return _layout;
        var typeface = new Typeface(GetValue(TextElement.FontFamilyProperty));
        var text = new FormattedText(row.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, GetValue(TextElement.FontSizeProperty),
            Brush(accent ?? "TextPrimaryBrush"))
        {
            MaxTextWidth = Math.Max(1, width),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis
        };
        if (row.HasMatch && row.MatchText.Length > 0 && row.MatchBefore.Length + row.MatchText.Length <= row.Name.Length)
        {
            text.SetForegroundBrush(Brush("AccentBrush"), row.MatchBefore.Length, row.MatchText.Length);
            text.SetFontWeight(FontWeight.SemiBold, row.MatchBefore.Length, row.MatchText.Length);
        }

        _layout = text;
        _layoutWidth = width;
        return text;
    }

    private IBrush? Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : null;

    private void SetHover(HierarchyRowZone zone)
    {
        if (zone == _hover)
            return;
        _hover = zone;
        Cursor = zone == HierarchyRowZone.None ? null : Hand;
        ToolTip.SetTip(this, zone switch
        {
            HierarchyRowZone.Visibility => "Show or hide in the viewport",
            HierarchyRowZone.Lock => "Lock or unlock picking in the viewport",
            _ => _row?.HasOverrides == true ? "Differs from its prefab" : null
        });
        InvalidateVisual();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HierarchyRow.Name) or nameof(HierarchyRow.MatchBefore) or nameof(HierarchyRow.MatchText) or nameof(HierarchyRow.IsPrefab)
            or nameof(HierarchyRow.IsPrefabMissing) or nameof(HierarchyRow.IsPrefabInstance))
            _layout = null;
        InvalidateVisual();
    }
}
