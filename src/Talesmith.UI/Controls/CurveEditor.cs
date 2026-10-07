using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Talesmith.Mathematics;

namespace Talesmith.UI.Controls;

/// <summary>Edits a <see cref="Curve"/>: a <see cref="CurveCanvas"/> with presets, view fitting and fields for the selected key.</summary>
[TemplatePart(CanvasPart, typeof(CurveCanvas))]
[TemplatePart(PresetsPart, typeof(Panel))]
[TemplatePart(FitPart, typeof(Button))]
[TemplatePart(InterpolationPart, typeof(SegmentedControl))]
[TemplatePart(TimePart, typeof(NumberField))]
[TemplatePart(ValuePart, typeof(NumberField))]
[TemplatePart(DeletePart, typeof(Button))]
[PseudoClasses(":has-selection")]
public class CurveEditor : TemplatedControl
{
    private const string CanvasPart = "PART_Canvas";
    private const string PresetsPart = "PART_Presets";
    private const string FitPart = "PART_Fit";
    private const string InterpolationPart = "PART_Interpolation";
    private const string TimePart = "PART_Time";
    private const string ValuePart = "PART_Value";
    private const string DeletePart = "PART_Delete";

    private static readonly CurveInterpolation[] Interpolations = [CurveInterpolation.Smooth, CurveInterpolation.Linear, CurveInterpolation.Constant];

    public static readonly StyledProperty<Curve> CurveProperty =
        CurveCanvas.CurveProperty.AddOwner<CurveEditor>(new StyledPropertyMetadata<Curve>(CurvePresets.Constant, BindingMode.TwoWay));

    private CurveCanvas? _canvas;
    private Panel? _presets;
    private Button? _fit;
    private SegmentedControl? _interpolation;
    private NumberField? _time;
    private NumberField? _value;
    private Button? _delete;
    private bool _syncing;

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

    public Curve Curve
    {
        get => GetValue(CurveProperty);
        set => SetValue(CurveProperty, value);
    }

    /// <summary>Gets the plot, once the template is applied.</summary>
    public CurveCanvas? Canvas => _canvas;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        Detach();

        _canvas = e.NameScope.Find<CurveCanvas>(CanvasPart);
        _presets = e.NameScope.Find<Panel>(PresetsPart);
        _fit = e.NameScope.Find<Button>(FitPart);
        _interpolation = e.NameScope.Find<SegmentedControl>(InterpolationPart);
        _time = e.NameScope.Find<NumberField>(TimePart);
        _value = e.NameScope.Find<NumberField>(ValuePart);
        _delete = e.NameScope.Find<Button>(DeletePart);

        if (_canvas is not null)
        {
            _canvas.Curve = Curve;
            _canvas.PropertyChanged += OnCanvasPropertyChanged;
        }

        _fit?.Click += OnFitClick;
        _delete?.Click += OnDeleteClick;
        _time?.PropertyChanged += OnKeyFieldChanged;
        _value?.PropertyChanged += OnKeyFieldChanged;
        if (_interpolation is not null)
        {
            _interpolation.ItemsSource = new[] { "Smooth", "Linear", "Constant" };
            _interpolation.SelectionChanged += OnInterpolationChanged;
        }

        if (_presets is not null)
        {
            _presets.Children.Clear();
            foreach (var preset in CurvePresets.All)
                _presets.Children.Add(CreatePresetButton(preset));
        }

        SyncSelection();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CurveProperty && _canvas is not null && !ReferenceEquals(_canvas.Curve, Curve))
            _canvas.Curve = Curve;
    }

    private void Detach()
    {
        _canvas?.PropertyChanged -= OnCanvasPropertyChanged;
        _fit?.Click -= OnFitClick;
        _delete?.Click -= OnDeleteClick;
        _time?.PropertyChanged -= OnKeyFieldChanged;
        _value?.PropertyChanged -= OnKeyFieldChanged;
        _interpolation?.SelectionChanged -= OnInterpolationChanged;
    }

    private Button CreatePresetButton(CurvePreset preset)
    {
        var button = new Button
        {
            Classes = { "curve-preset" },
            Content = new CurveSwatch { Curve = preset.Curve, Classes = { "preset" } }
        };
        ToolTip.SetTip(button, preset.Name);
        button.Click += (_, _) => _canvas?.ApplyCurve(preset.Curve);
        return button;
    }

    private void OnCanvasPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == CurveCanvas.CurveProperty)
        {
            SetCurrentValue(CurveProperty, _canvas!.Curve);
            SyncSelection();
        }
        else if (e.Property == CurveCanvas.SelectedKeyIndexProperty)
        {
            SyncSelection();
        }
    }

    private void SyncSelection()
    {
        if (_canvas is null)
            return;

        var index = _canvas.SelectedKeyIndex;
        var keys = _canvas.Curve.Keys;
        var hasKey = index >= 0 && index < keys.Length;
        PseudoClasses.Set(":has-selection", hasKey);

        _syncing = true;
        if (hasKey)
        {
            var key = keys[index];
            _time?.Value = key.Time;
            _value?.Value = key.Value;
            _interpolation?.SelectedIndex = Array.IndexOf(Interpolations, key.Interpolation);
        }
        else
        {
            _interpolation?.SelectedIndex = -1;
        }

        _time?.IsEnabled = hasKey;
        _value?.IsEnabled = hasKey;
        _interpolation?.IsEnabled = hasKey;
        _delete?.IsEnabled = hasKey && keys.Length > 1;
        _syncing = false;
    }

    private void OnKeyFieldChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_syncing || e.Property != NumberField.ValueProperty || _canvas is null || _time is null || _value is null)
            return;
        var index = _canvas.SelectedKeyIndex;
        if (index < 0 || index >= _canvas.Curve.Keys.Length)
            return;
        _canvas.Curve = CurveEditing.MoveKey(_canvas.Curve, index, (float)_time.Value, (float)_value.Value);
    }

    private void OnInterpolationChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _interpolation is not { SelectedIndex: >= 0 and var selected })
            return;
        _canvas?.SetSelectedInterpolation(Interpolations[selected]);
    }

    private void OnFitClick(object? sender, RoutedEventArgs e) => _canvas?.FitView();

    private void OnDeleteClick(object? sender, RoutedEventArgs e) => _canvas?.DeleteSelectedKey();
}
