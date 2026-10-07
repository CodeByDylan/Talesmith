using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Talesmith.UI.Controls;

/// <summary>Shared settings of property rows, such as the label column width all rows in a container use.</summary>
public static class PropertyGrid
{
    /// <summary>The width of the label column, inherited by every <see cref="PropertyRow"/> below the element it is set on.</summary>
    public static readonly AttachedProperty<double> LabelWidthProperty =
        AvaloniaProperty.RegisterAttached<Control, double>("LabelWidth", typeof(PropertyGrid), 120, inherits: true);

    public static double GetLabelWidth(Control element) => element.GetValue(LabelWidthProperty);

    public static void SetLabelWidth(Control element, double value) => element.SetValue(LabelWidthProperty, value);
}

/// <summary>An inspector row: a label with a tooltip in a column of shared, resizable width, and the value editor beside it.</summary>
/// <remarks>Rows whose <see cref="IsModified"/> is set show an accent marker and a reset button that raises <see cref="ResetRequested"/>. In a narrow
/// row the label column gives up width, down to its minimum, so that the value keeps room to show.</remarks>
[TemplatePart(ResizerPart, typeof(Control))]
[TemplatePart(ResetPart, typeof(Button))]
[PseudoClasses(":modified", ":resizing")]
public class PropertyRow : ContentControl
{
    private const string ResizerPart = "PART_Resizer";
    private const string ResetPart = "PART_Reset";
    private const double MinimumLabelWidth = 48;

    /// <summary>The width the value keeps in a narrow row, enough for a field of two numbers.</summary>
    private const double ValueRoom = 136;

    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<PropertyRow, string?>(nameof(Label));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<PropertyRow, string?>(nameof(Description));

    public static readonly StyledProperty<double> LabelWidthProperty =
        PropertyGrid.LabelWidthProperty.AddOwner<PropertyRow>();

    public static readonly DirectProperty<PropertyRow, double> LabelColumnWidthProperty =
        AvaloniaProperty.RegisterDirect<PropertyRow, double>(nameof(LabelColumnWidth), row => row.LabelColumnWidth);

    public static readonly StyledProperty<object?> AccessoryProperty =
        AvaloniaProperty.Register<PropertyRow, object?>(nameof(Accessory));

    public static readonly StyledProperty<bool> IsModifiedProperty =
        AvaloniaProperty.Register<PropertyRow, bool>(nameof(IsModified));

    public static readonly StyledProperty<ICommand?> ResetCommandProperty =
        AvaloniaProperty.Register<PropertyRow, ICommand?>(nameof(ResetCommand));

    public static readonly RoutedEvent<RoutedEventArgs> ResetRequestedEvent =
        RoutedEvent.Register<PropertyRow, RoutedEventArgs>(nameof(ResetRequested), RoutingStrategies.Bubble);

    private Control? _resizer;
    private Button? _reset;
    private double _resizeStart;
    private double _widthStart;
    private Control? _widthOwner;
    private double _labelColumnWidth = PropertyGrid.LabelWidthProperty.GetDefaultValue(typeof(PropertyRow));

    /// <summary>Raised when the reset button is clicked, after <see cref="ResetCommand"/> runs.</summary>
    public event EventHandler<RoutedEventArgs>? ResetRequested
    {
        add => AddHandler(ResetRequestedEvent, value);
        remove => RemoveHandler(ResetRequestedEvent, value);
    }

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>Gets or sets the tooltip of the label, typically what the property does.</summary>
    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>Gets or sets the label column width; inherited from <see cref="PropertyGrid.LabelWidthProperty"/>.</summary>
    public double LabelWidth
    {
        get => GetValue(LabelWidthProperty);
        set => SetValue(LabelWidthProperty, value);
    }

    /// <summary>Gets the width the label column takes: <see cref="LabelWidth"/>, or less in a row too narrow for it and the value.</summary>
    public double LabelColumnWidth
    {
        get => _labelColumnWidth;
        private set => SetAndRaise(LabelColumnWidthProperty, ref _labelColumnWidth, value);
    }

    /// <summary>Gets or sets a small control shown at the end of the label, such as a chip saying the value follows another.</summary>
    public object? Accessory
    {
        get => GetValue(AccessoryProperty);
        set => SetValue(AccessoryProperty, value);
    }

    /// <summary>Gets or sets whether the value differs from its default or prefab value.</summary>
    public bool IsModified
    {
        get => GetValue(IsModifiedProperty);
        set => SetValue(IsModifiedProperty, value);
    }

    /// <summary>Gets or sets the command run by the reset button.</summary>
    public ICommand? ResetCommand
    {
        get => GetValue(ResetCommandProperty);
        set => SetValue(ResetCommandProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_resizer is not null)
        {
            _resizer.PointerPressed -= OnResizerPressed;
            _resizer.PointerMoved -= OnResizerMoved;
            _resizer.PointerReleased -= OnResizerReleased;
        }

        _reset?.Click -= OnResetClick;
        _resizer = e.NameScope.Find<Control>(ResizerPart);
        _reset = e.NameScope.Find<Button>(ResetPart);
        _reset?.Click += OnResetClick;

        if (_resizer is not null)
        {
            _resizer.PointerPressed += OnResizerPressed;
            _resizer.PointerMoved += OnResizerMoved;
            _resizer.PointerReleased += OnResizerReleased;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsModifiedProperty)
            PseudoClasses.Set(":modified", IsModified);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        LabelColumnWidth = double.IsFinite(availableSize.Width)
            ? Math.Min(LabelWidth, Math.Max(MinimumLabelWidth, Math.Floor(availableSize.Width - ValueRoom)))
            : LabelWidth;
        return base.MeasureOverride(availableSize);
    }

    private void OnResetClick(object? sender, RoutedEventArgs e)
    {
        if (ResetCommand is { } command && command.CanExecute(null))
            command.Execute(null);
        RaiseEvent(new RoutedEventArgs(ResetRequestedEvent, this));
    }

    private void OnResizerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        _widthOwner = FindWidthOwner();
        _resizeStart = e.GetPosition(this).X;
        _widthStart = LabelColumnWidth;
        PseudoClasses.Set(":resizing", true);
        e.Pointer.Capture(_resizer);
        e.Handled = true;
    }

    private void OnResizerMoved(object? sender, PointerEventArgs e)
    {
        if (_widthOwner is null)
            return;
        var maximum = Math.Max(MinimumLabelWidth, Bounds.Width - ValueRoom);
        var width = Math.Round(Math.Clamp(_widthStart + e.GetPosition(this).X - _resizeStart, MinimumLabelWidth, maximum));
        _widthOwner.SetValue(PropertyGrid.LabelWidthProperty, width);
        e.Handled = true;
    }

    private void OnResizerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_widthOwner is null)
            return;
        _widthOwner = null;
        PseudoClasses.Set(":resizing", false);
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private Control FindWidthOwner()
    {
        Control? outermostGroup = null;
        foreach (var ancestor in this.GetVisualAncestors().OfType<Control>())
        {
            if (ancestor.IsSet(PropertyGrid.LabelWidthProperty))
                return ancestor;
            if (ancestor is PropertyGroup)
                outermostGroup = ancestor;
        }

        return outermostGroup?.GetVisualParent() as Control ?? outermostGroup ?? this.GetVisualParent() as Control ?? this;
    }
}
