using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Talesmith.Mathematics;

namespace Talesmith.UI.Controls;

/// <summary>A compact curve swatch for inspector rows that opens a <see cref="CurveEditor"/> in a flyout.</summary>
[TemplatePart(ButtonPart, typeof(Button))]
public class CurvePreview : TemplatedControl
{
    private const string ButtonPart = "PART_Button";

    public static readonly StyledProperty<Curve> CurveProperty =
        CurveCanvas.CurveProperty.AddOwner<CurvePreview>(new StyledPropertyMetadata<Curve>(CurvePresets.Constant, BindingMode.TwoWay));

    private Button? _button;
    private CurveEditor? _editor;

    /// <summary>Raised before the first change of an edit in the flyout.</summary>
    public event EventHandler<RoutedEventArgs>? EditStarted
    {
        add => AddHandler(ValueEdit.StartedEvent, value);
        remove => RemoveHandler(ValueEdit.StartedEvent, value);
    }

    /// <summary>Raised after the last change of an edit in the flyout.</summary>
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

    /// <summary>Opens the editor flyout.</summary>
    public void OpenEditor() => _button?.Flyout?.ShowAt(_button);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _button = e.NameScope.Find<Button>(ButtonPart);
        if (_button is null)
            return;

        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft, Content = new Panel() };
        flyout.Opening += (_, _) =>
        {
            if (_editor is null)
            {
                _editor = new CurveEditor { Curve = Curve };
                _editor.PropertyChanged += OnEditorPropertyChanged;
                _editor.AddHandler(ValueEdit.StartedEvent, ForwardEditEvent);
                _editor.AddHandler(ValueEdit.CompletedEvent, ForwardEditEvent);
                flyout.Content = _editor;
            }

            _editor.Curve = Curve;
        };
        _button.Flyout = flyout;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CurveProperty && _editor is not null && !ReferenceEquals(_editor.Curve, Curve))
            _editor.Curve = Curve;
    }

    private void OnEditorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == CurveEditor.CurveProperty)
            SetCurrentValue(CurveProperty, _editor!.Curve);
    }

    private void ForwardEditEvent(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        RaiseEvent(new RoutedEventArgs(e.RoutedEvent, this));
    }
}
