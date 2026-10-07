using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Talesmith.Editor.Assets.SpriteEditor;

/// <summary>The sprite editor's view: keeps the sprite list and canvas selection together, the pivot presets, keyboard shortcuts and
/// reordering frames by dragging.</summary>
/// <remarks>In a narrow editor the slicing and sprite columns give up width in proportion, down to their minimums, so that the canvas keeps room;
/// widths set by dragging their splitters are the ones they give up from.</remarks>
public partial class SpriteEditorView : UserControl
{
    private const double CanvasMinimum = 320;
    private const double SlicingMinimum = 190;
    private const double SpritesMinimum = 170;

    private static readonly int[] PresetOrder = [2, 3, 4, 5, 0, 6, 7, 1, 8];

    private SpriteEditorViewModel? _viewModel;
    private bool _syncing;
    private FrameItem? _draggedFrame;
    private Border? _dropTarget;
    private double _slicingWidth = 270;
    private double _spritesWidth = 260;
    private bool _fitting;

    public SpriteEditorView()
    {
        InitializeComponent();
        foreach (var index in PresetOrder)
        {
            var preset = PivotPreset.All[index];
            var button = new Button
            {
                Classes = { "pivot" },
                Content = new Ellipse { Width = 6, Height = 6, Fill = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                Focusable = false
            };
            ToolTip.SetTip(button, preset.Name);
            button.Click += (_, _) => _viewModel?.ApplyPivot(preset);
            PivotGrid.Children.Add(button);
        }

        SliceList.SelectionChanged += OnListSelectionChanged;
        FrameStrip.AddHandler(PointerPressedEvent, OnFramePressed, RoutingStrategies.Tunnel);
        FrameStrip.AddHandler(PointerMovedEvent, OnFrameMoved, RoutingStrategies.Tunnel);
        FrameStrip.AddHandler(PointerReleasedEvent, OnFrameReleased, RoutingStrategies.Tunnel);
        Body.ColumnDefinitions[0].PropertyChanged += (_, e) => RememberWidth(e, ref _slicingWidth);
        Body.ColumnDefinitions[4].PropertyChanged += (_, e) => RememberWidth(e, ref _spritesWidth);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        FitColumns(e.NewSize.Width);
    }

    private void RememberWidth(AvaloniaPropertyChangedEventArgs e, ref double width)
    {
        if (!_fitting && e.Property == ColumnDefinition.WidthProperty && e.NewValue is GridLength { IsAbsolute: true } length)
            width = length.Value;
    }

    /// <summary>Narrows the slicing and sprite columns in proportion so that the canvas keeps <see cref="CanvasMinimum"/>, down to their minimums.</summary>
    private void FitColumns(double width)
    {
        var scale = Math.Clamp((width - CanvasMinimum) / (_slicingWidth + _spritesWidth), 0, 1);
        _fitting = true;
        Body.ColumnDefinitions[0].Width = new GridLength(Math.Max(SlicingMinimum, Math.Round(_slicingWidth * scale)));
        Body.ColumnDefinitions[4].Width = new GridLength(Math.Max(SpritesMinimum, Math.Round(_spritesWidth * scale)));
        _fitting = false;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
            _viewModel.SlicesChanged -= OnSlicesChanged;
        _viewModel = DataContext as SpriteEditorViewModel;
        if (_viewModel is not null)
            _viewModel.SlicesChanged += OnSlicesChanged;
    }

    private void OnSlicesChanged(object? sender, EventArgs e)
    {
        if (_viewModel is null || SliceList.SelectedItems is not { } items)
            return;
        _syncing = true;
        items.Clear();
        foreach (var slice in _viewModel.Slices.Where(s => s.IsSelected))
            items.Add(slice);
        if (_viewModel.Primary is { } primary)
            SliceList.ScrollIntoView(primary);
        _syncing = false;
    }

    private void OnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _viewModel is null || SliceList.SelectedItems is not { } items)
            return;
        var selected = items.OfType<SliceItem>().ToHashSet();
        foreach (var slice in _viewModel.Slices)
            slice.IsSelected = selected.Contains(slice);
        var last = e.AddedItems.OfType<SliceItem>().LastOrDefault();
        _syncing = true;
        _viewModel.Select(last?.Name ?? selected.LastOrDefault()?.Name, toggle: false, add: true);
        _syncing = false;
        Canvas.InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_viewModel is null || e.Handled || e.Source is TextBox)
            return;
        var ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        var step = (e.KeyModifiers & KeyModifiers.Shift) != 0 ? 10 : 1;
        switch (e.Key)
        {
            case Key.Delete or Key.Back:
                _viewModel.DeleteSelected();
                break;
            case Key.A when ctrl:
                _viewModel.SelectAll();
                break;
            case Key.Escape:
                _viewModel.Select(null);
                break;
            case Key.Left:
                _viewModel.Nudge(-step, 0);
                break;
            case Key.Right:
                _viewModel.Nudge(step, 0);
                break;
            case Key.Up:
                _viewModel.Nudge(0, -step);
                break;
            case Key.Down:
                _viewModel.Nudge(0, step);
                break;
            case Key.Space:
                _viewModel.TogglePlay();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    // Reordering frames

    private static Border? FrameAt(object? source) =>
        (source as Visual)?.GetSelfAndVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("frame"));

    private void OnFramePressed(object? sender, PointerPressedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is not null)
            return;
        if (FrameAt(e.Source)?.DataContext is FrameItem frame && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _draggedFrame = frame;
            e.Pointer.Capture(FrameStrip);
        }
    }

    private void OnFrameMoved(object? sender, PointerEventArgs e)
    {
        if (_draggedFrame is null)
            return;
        var target = FrameStrip.InputHitTest(e.GetPosition(FrameStrip)) is { } hit ? FrameAt(hit) : null;
        if (ReferenceEquals(target, _dropTarget))
            return;
        _dropTarget?.Classes.Set("drop-before", false);
        _dropTarget = target is { DataContext: FrameItem f } && f != _draggedFrame ? target : null;
        _dropTarget?.Classes.Set("drop-before", true);
    }

    private void OnFrameReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggedFrame is not { } frame)
            return;
        e.Pointer.Capture(null);
        _draggedFrame = null;
        if (_dropTarget is { DataContext: FrameItem target })
        {
            _dropTarget.Classes.Set("drop-before", false);
            _viewModel?.MoveFrame(frame.Index, target.Index > frame.Index ? target.Index - 1 : target.Index);
        }

        _dropTarget = null;
    }
}
