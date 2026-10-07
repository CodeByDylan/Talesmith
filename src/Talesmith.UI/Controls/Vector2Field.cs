using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;

namespace Talesmith.UI.Controls;

/// <summary>Edits a <see cref="Vector2"/> with two <see cref="NumberField"/>s labelled X and Y in the axis colors.</summary>
[TemplatePart(XPart, typeof(NumberField))]
[TemplatePart(YPart, typeof(NumberField))]
public class Vector2Field : TemplatedControl
{
    private const string XPart = "PART_X";
    private const string YPart = "PART_Y";

    public static readonly StyledProperty<Vector2> ValueProperty =
        AvaloniaProperty.Register<Vector2Field, Vector2>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> MinimumProperty =
        NumberField.MinimumProperty.AddOwner<Vector2Field>();

    public static readonly StyledProperty<double> MaximumProperty =
        NumberField.MaximumProperty.AddOwner<Vector2Field>();

    public static readonly StyledProperty<double> StepProperty =
        NumberField.StepProperty.AddOwner<Vector2Field>();

    public static readonly StyledProperty<string> FormatStringProperty =
        NumberField.FormatStringProperty.AddOwner<Vector2Field>();

    public static readonly StyledProperty<string?> SuffixProperty =
        NumberField.SuffixProperty.AddOwner<Vector2Field>();

    public static readonly StyledProperty<bool> IsXMixedProperty =
        AvaloniaProperty.Register<Vector2Field, bool>(nameof(IsXMixed));

    public static readonly StyledProperty<bool> IsYMixedProperty =
        AvaloniaProperty.Register<Vector2Field, bool>(nameof(IsYMixed));

    private NumberField? _x;
    private NumberField? _y;
    private bool _syncing;

    /// <summary>Raised before the first change of an edit of either component.</summary>
    public event EventHandler<RoutedEventArgs>? EditStarted
    {
        add => AddHandler(ValueEdit.StartedEvent, value);
        remove => RemoveHandler(ValueEdit.StartedEvent, value);
    }

    /// <summary>Raised after the last change of an edit of either component.</summary>
    public event EventHandler<RoutedEventArgs>? EditCompleted
    {
        add => AddHandler(ValueEdit.CompletedEvent, value);
        remove => RemoveHandler(ValueEdit.CompletedEvent, value);
    }

    public Vector2 Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Step
    {
        get => GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public string FormatString
    {
        get => GetValue(FormatStringProperty);
        set => SetValue(FormatStringProperty, value);
    }

    public string? Suffix
    {
        get => GetValue(SuffixProperty);
        set => SetValue(SuffixProperty, value);
    }

    /// <summary>Gets or sets whether X differs across the edited objects.</summary>
    public bool IsXMixed
    {
        get => GetValue(IsXMixedProperty);
        set => SetValue(IsXMixedProperty, value);
    }

    /// <summary>Gets or sets whether Y differs across the edited objects.</summary>
    public bool IsYMixed
    {
        get => GetValue(IsYMixedProperty);
        set => SetValue(IsYMixedProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _x?.PropertyChanged -= OnComponentChanged;
        _y?.PropertyChanged -= OnComponentChanged;
        _x = e.NameScope.Find<NumberField>(XPart);
        _y = e.NameScope.Find<NumberField>(YPart);
        _x?.PropertyChanged += OnComponentChanged;
        _y?.PropertyChanged += OnComponentChanged;
        Sync();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty || change.Property == IsXMixedProperty || change.Property == IsYMixedProperty)
            Sync();
    }

    private void Sync()
    {
        if (_x is null || _y is null)
            return;
        _syncing = true;
        _x.Value = Value.X;
        _y.Value = Value.Y;
        _x.IsMixed = IsXMixed;
        _y.IsMixed = IsYMixed;
        _syncing = false;
    }

    private void OnComponentChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_syncing || _x is null || _y is null)
            return;
        if (e.Property == NumberField.ValueProperty)
        {
            SetCurrentValue(ValueProperty, new Vector2((float)_x.Value, (float)_y.Value));
        }
        else if (e.Property == NumberField.IsMixedProperty)
        {
            SetCurrentValue(IsXMixedProperty, _x.IsMixed);
            SetCurrentValue(IsYMixedProperty, _y.IsMixed);
        }
    }
}
