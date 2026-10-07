using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Talesmith.Mathematics;

namespace Talesmith.UI.Controls;

/// <summary>Edits a <see cref="Gradient"/>: a <see cref="GradientBar"/> of stops, the selected stop's location and a color picker for its color.</summary>
[TemplatePart(BarPart, typeof(GradientBar))]
[TemplatePart(LocationPart, typeof(NumberField))]
[TemplatePart(PickerPart, typeof(ColorPicker))]
[TemplatePart(DeletePart, typeof(Button))]
[PseudoClasses(":has-selection")]
public class GradientEditor : TemplatedControl
{
    private const string BarPart = "PART_Bar";
    private const string LocationPart = "PART_Location";
    private const string PickerPart = "PART_Picker";
    private const string DeletePart = "PART_Delete";

    public static readonly StyledProperty<Gradient> GradientProperty =
        GradientBar.GradientProperty.AddOwner<GradientEditor>(new StyledPropertyMetadata<Gradient>(Gradient.White, BindingMode.TwoWay));

    private GradientBar? _bar;
    private NumberField? _location;
    private ColorPicker? _picker;
    private Button? _delete;
    private bool _syncing;
    private bool _pickerEditing;

    /// <summary>Raised before the first change of an edit.</summary>
    public event EventHandler<RoutedEventArgs>? EditStarted
    {
        add => AddHandler(ValueEdit.StartedEvent, value);
        remove => RemoveHandler(ValueEdit.StartedEvent, value);
    }

    /// <summary>Raised after the last change of an edit.</summary>
    public event EventHandler<RoutedEventArgs>? EditCompleted
    {
        add => AddHandler(ValueEdit.CompletedEvent, value);
        remove => RemoveHandler(ValueEdit.CompletedEvent, value);
    }

    public Gradient Gradient
    {
        get => GetValue(GradientProperty);
        set => SetValue(GradientProperty, value);
    }

    /// <summary>Gets the stops bar, once the template is applied.</summary>
    public GradientBar? Bar => _bar;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        Detach();

        _bar = e.NameScope.Find<GradientBar>(BarPart);
        _location = e.NameScope.Find<NumberField>(LocationPart);
        _picker = e.NameScope.Find<ColorPicker>(PickerPart);
        _delete = e.NameScope.Find<Button>(DeletePart);

        if (_bar is not null)
        {
            _bar.Gradient = Gradient;
            if (_bar.SelectedStopIndex < 0 && Gradient.Stops.Length > 0)
                _bar.SelectedStopIndex = 0;
            _bar.PropertyChanged += OnBarPropertyChanged;
        }

        _location?.PropertyChanged += OnLocationChanged;
        if (_picker is not null)
        {
            _picker.PropertyChanged += OnPickerChanged;
            _picker.AddHandler(PointerPressedEvent, OnPickerPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
            _picker.AddHandler(PointerReleasedEvent, OnPickerPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        }

        _delete?.Click += OnDeleteClick;
        Sync();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == GradientProperty && _bar is not null && !ReferenceEquals(_bar.Gradient, Gradient))
            _bar.Gradient = Gradient;
    }

    private void Detach()
    {
        _bar?.PropertyChanged -= OnBarPropertyChanged;
        _location?.PropertyChanged -= OnLocationChanged;
        _picker?.PropertyChanged -= OnPickerChanged;
        _picker?.RemoveHandler(PointerPressedEvent, OnPickerPointerPressed);
        _picker?.RemoveHandler(PointerReleasedEvent, OnPickerPointerReleased);
        _delete?.Click -= OnDeleteClick;
    }

    private void OnBarPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == GradientBar.GradientProperty)
        {
            SetCurrentValue(GradientProperty, _bar!.Gradient);
            Sync();
        }
        else if (e.Property == GradientBar.SelectedStopIndexProperty)
        {
            Sync();
        }
    }

    private void Sync()
    {
        if (_bar is null)
            return;
        var index = _bar.SelectedStopIndex;
        var stops = _bar.Gradient.Stops;
        var hasStop = index >= 0 && index < stops.Length;
        PseudoClasses.Set(":has-selection", hasStop);

        _syncing = true;
        if (hasStop)
        {
            _location?.Value = Math.Round(stops[index].Position * 100, 1);
            if (_picker is not null && !_pickerEditing)
            {
                var color = stops[index].Color.ToAvalonia();
                if (_picker.Color != color)
                {
                    _picker.OriginalColor = color;
                    _picker.Color = color;
                }
            }
        }

        _location?.IsEnabled = hasStop;
        _picker?.IsEnabled = hasStop;
        _delete?.IsEnabled = hasStop && stops.Length > 1;
        _syncing = false;
    }

    private void OnLocationChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_syncing || e.Property != NumberField.ValueProperty || _location is null)
            return;
        _bar?.SetSelectedPosition((float)(_location.Value / 100));
    }

    private void OnPickerChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_syncing || e.Property != ColorPicker.ColorProperty || _picker is null || _bar is null)
            return;
        if (_pickerEditing)
            _bar.SetSelectedColor(_picker.Color.ToEngine());
        else
            ValueEdit.Apply(this, () => _bar.SetSelectedColor(_picker.Color.ToEngine()));
    }

    private void OnPickerPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        _pickerEditing = true;
        ValueEdit.RaiseStarted(this);
    }

    private void OnPickerPointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (!_pickerEditing)
            return;
        _pickerEditing = false;
        ValueEdit.RaiseCompleted(this);
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e) => _bar?.RemoveSelectedStop();
}
