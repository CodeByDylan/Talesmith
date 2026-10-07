using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Talesmith.UI.Controls;

/// <summary>A compact numeric field: drag it to scrub, click it to type a value or an expression such as <c>16*2+4</c>.</summary>
/// <remarks>
/// Dragging moves one <see cref="Step"/> per pixel; Shift is ten times finer and Ctrl ten times coarser. Up and Down step the value
/// while typing. Enter or leaving the field commits, Escape reverts. Each drag, step or commit raises <see cref="ValueEdit.StartedEvent"/>
/// and <see cref="ValueEdit.CompletedEvent"/> around its changes.
/// </remarks>
[TemplatePart(TextBoxPart, typeof(TextBox))]
[TemplatePart(LabelPart, typeof(Control))]
[PseudoClasses(":editing", ":scrubbing", ":mixed")]
public class NumberField : TemplatedControl
{
    private const string TextBoxPart = "PART_TextBox";
    private const string LabelPart = "PART_Label";
    private const double DragThreshold = 3;

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<NumberField, double>(nameof(Value), defaultBindingMode: BindingMode.TwoWay, coerce: CoerceValue);

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<NumberField, double>(nameof(Minimum), double.NegativeInfinity);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<NumberField, double>(nameof(Maximum), double.PositiveInfinity);

    public static readonly StyledProperty<double> StepProperty =
        AvaloniaProperty.Register<NumberField, double>(nameof(Step), 1);

    public static readonly StyledProperty<string> FormatStringProperty =
        AvaloniaProperty.Register<NumberField, string>(nameof(FormatString), "0.###");

    public static readonly StyledProperty<bool> IsIntegerProperty =
        AvaloniaProperty.Register<NumberField, bool>(nameof(IsInteger));

    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<NumberField, string?>(nameof(Label));

    public static readonly StyledProperty<IBrush?> LabelForegroundProperty =
        AvaloniaProperty.Register<NumberField, IBrush?>(nameof(LabelForeground));

    public static readonly StyledProperty<string?> SuffixProperty =
        AvaloniaProperty.Register<NumberField, string?>(nameof(Suffix));

    public static readonly StyledProperty<bool> IsMixedProperty =
        AvaloniaProperty.Register<NumberField, bool>(nameof(IsMixed));

    private TextBox? _textBox;
    private Control? _label;
    private bool _pressed;
    private bool _scrubbing;
    private Point _anchor;
    private double _anchorValue;
    private double _factor = 1;

    static NumberField()
    {
        FocusableProperty.OverrideDefaultValue<NumberField>(true);
        IsTabStopProperty.OverrideDefaultValue<NumberField>(false);
    }

    /// <summary>Raised before the first change of a drag, step or typed commit.</summary>
    public event EventHandler<RoutedEventArgs>? EditStarted
    {
        add => AddHandler(ValueEdit.StartedEvent, value);
        remove => RemoveHandler(ValueEdit.StartedEvent, value);
    }

    /// <summary>Raised after the last change of a drag, step or typed commit.</summary>
    public event EventHandler<RoutedEventArgs>? EditCompleted
    {
        add => AddHandler(ValueEdit.CompletedEvent, value);
        remove => RemoveHandler(ValueEdit.CompletedEvent, value);
    }

    public double Value
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

    /// <summary>Gets or sets the change per pixel dragged and per arrow key.</summary>
    public double Step
    {
        get => GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    /// <summary>Gets or sets the .NET format used to display the value, such as <c>0.##</c>.</summary>
    public string FormatString
    {
        get => GetValue(FormatStringProperty);
        set => SetValue(FormatStringProperty, value);
    }

    /// <summary>Gets or sets whether the value is rounded to whole numbers.</summary>
    public bool IsInteger
    {
        get => GetValue(IsIntegerProperty);
        set => SetValue(IsIntegerProperty, value);
    }

    /// <summary>Gets or sets the short text, such as an axis name, shown at the left; dragging it scrubs even while typing.</summary>
    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public IBrush? LabelForeground
    {
        get => GetValue(LabelForegroundProperty);
        set => SetValue(LabelForegroundProperty, value);
    }

    /// <summary>Gets or sets the unit shown after the value, such as <c>px</c> or <c>°</c>.</summary>
    public string? Suffix
    {
        get => GetValue(SuffixProperty);
        set => SetValue(SuffixProperty, value);
    }

    /// <summary>Gets or sets whether the field stands for several different values, shown as a dash until edited.</summary>
    public bool IsMixed
    {
        get => GetValue(IsMixedProperty);
        set => SetValue(IsMixedProperty, value);
    }

    /// <summary>Gets whether the user is dragging the value.</summary>
    public bool IsScrubbing => _scrubbing;

    /// <summary>Focuses the text and selects it for typing.</summary>
    public void BeginTextEdit()
    {
        if (_textBox is null)
            return;
        _textBox.Focus(NavigationMethod.Pointer);
        _textBox.SelectAll();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_textBox is not null)
        {
            _textBox.RemoveHandler(KeyDownEvent, OnTextKeyDown);
            _textBox.GotFocus -= OnTextGotFocus;
            _textBox.LostFocus -= OnTextLostFocus;
        }

        _textBox = e.NameScope.Find<TextBox>(TextBoxPart);
        _label = e.NameScope.Find<Control>(LabelPart);

        if (_textBox is not null)
        {
            _textBox.AddHandler(KeyDownEvent, OnTextKeyDown, RoutingStrategies.Tunnel);
            _textBox.GotFocus += OnTextGotFocus;
            _textBox.LostFocus += OnTextLostFocus;
            InputFilter.SetMode(_textBox, InputFilterMode.None);
        }

        UpdateText();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ValueProperty || change.Property == FormatStringProperty || change.Property == IsIntegerProperty)
        {
            if (!IsEditing)
                UpdateText();
        }
        else if (change.Property == IsMixedProperty)
        {
            PseudoClasses.Set(":mixed", IsMixed);
            if (!IsEditing)
                UpdateText();
        }
        else if (change.Property == MinimumProperty || change.Property == MaximumProperty)
        {
            CoerceValue(ValueProperty);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !IsEffectivelyEnabled)
            return;

        if (IsEditing)
        {
            if (!IsOnLabel(e.Source))
                return;
            Commit();
            Focus();
        }

        _pressed = true;
        _anchor = e.GetPosition(this);
        _anchorValue = Value;
        _factor = NumberScrub.Factor(e.KeyModifiers);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_pressed)
            return;

        var position = e.GetPosition(this);
        if (!_scrubbing)
        {
            if (Math.Abs(position.X - _anchor.X) < DragThreshold)
                return;
            _scrubbing = true;
            _anchor = position;
            PseudoClasses.Set(":scrubbing", true);
            ValueEdit.RaiseStarted(this);
        }

        var factor = NumberScrub.Factor(e.KeyModifiers);
        if (factor != _factor)
        {
            _factor = factor;
            _anchor = position;
            _anchorValue = Value;
        }

        var value = NumberScrub.Drag(_anchorValue, position.X - _anchor.X, Step, _factor, Minimum, Maximum);
        SetCurrentValue(ValueProperty, IsInteger ? Math.Round(value) : value);
        IsMixed = false;
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_pressed)
            return;

        var wasScrubbing = _scrubbing;
        EndPointer();
        e.Pointer.Capture(null);
        if (!wasScrubbing)
            BeginTextEdit();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndPointer();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || IsEditing)
            return;

        switch (e.Key)
        {
            case Key.Up or Key.Right:
                StepBy(1, e.KeyModifiers);
                e.Handled = true;
                break;
            case Key.Down or Key.Left:
                StepBy(-1, e.KeyModifiers);
                e.Handled = true;
                break;
            case Key.Enter or Key.F2:
                BeginTextEdit();
                e.Handled = true;
                break;
        }
    }

    private bool IsEditing => _textBox?.IsKeyboardFocusWithin == true;

    private void EndPointer()
    {
        _pressed = false;
        if (!_scrubbing)
            return;
        _scrubbing = false;
        PseudoClasses.Set(":scrubbing", false);
        ValueEdit.RaiseCompleted(this);
    }

    private bool IsOnLabel(object? source) =>
        _label is not null && source is Visual visual && (ReferenceEquals(visual, _label) || _label.IsVisualAncestorOf(visual));

    private void OnTextKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Commit();
                _textBox?.SelectAll();
                e.Handled = true;
                break;
            case Key.Escape:
                UpdateText(editing: true);
                _textBox?.SelectAll();
                e.Handled = true;
                break;
            case Key.Up:
                Commit();
                StepBy(1, e.KeyModifiers);
                UpdateText(editing: true);
                _textBox?.SelectAll();
                e.Handled = true;
                break;
            case Key.Down:
                Commit();
                StepBy(-1, e.KeyModifiers);
                UpdateText(editing: true);
                _textBox?.SelectAll();
                e.Handled = true;
                break;
        }
    }

    private void OnTextGotFocus(object? sender, FocusChangedEventArgs e)
    {
        PseudoClasses.Set(":editing", true);
        UpdateText(editing: true);
        _textBox?.SelectAll();
    }

    private void OnTextLostFocus(object? sender, RoutedEventArgs e)
    {
        Commit();
        PseudoClasses.Set(":editing", false);
        UpdateText();
    }

    private void StepBy(int direction, KeyModifiers modifiers)
    {
        var value = NumberScrub.Step(Value, direction, Step, NumberScrub.Factor(modifiers), Minimum, Maximum);
        if (IsInteger)
            value = Math.Round(value);
        if (value.Equals(Value) && !IsMixed)
            return;
        ValueEdit.Apply(this, () =>
        {
            SetCurrentValue(ValueProperty, value);
            IsMixed = false;
        });
    }

    private void Commit()
    {
        if (_textBox is null)
            return;
        var text = _textBox.Text;
        if (text == Format(Value) && !IsMixed)
            return;
        if (!NumberExpression.TryEvaluate(text, out var parsed))
        {
            UpdateText(editing: true);
            return;
        }

        var value = Math.Clamp(IsInteger ? Math.Round(parsed) : parsed, Minimum, Maximum);
        if (value.Equals(Value) && !IsMixed)
        {
            UpdateText(editing: true);
            return;
        }

        ValueEdit.Apply(this, () =>
        {
            SetCurrentValue(ValueProperty, value);
            IsMixed = false;
        });
        UpdateText(editing: true);
    }

    private void UpdateText(bool editing = false)
    {
        if (_textBox is null)
            return;
        _textBox.Text = IsMixed ? editing ? string.Empty : "—" : Format(Value);
    }

    private string Format(double value) => IsInteger
        ? Math.Round(value).ToString("0", CultureInfo.CurrentCulture)
        : value.ToString(FormatString, CultureInfo.CurrentCulture);

    private static double CoerceValue(AvaloniaObject sender, double value)
    {
        var field = (NumberField)sender;
        if (double.IsNaN(value))
            return field.Value;
        return Math.Clamp(value, field.Minimum, Math.Max(field.Minimum, field.Maximum));
    }
}
