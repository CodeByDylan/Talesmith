using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Talesmith.Mathematics;

namespace Talesmith.UI.Controls;

/// <summary>A compact gradient swatch for inspector rows that opens a <see cref="GradientEditor"/> in a flyout.</summary>
[TemplatePart(ButtonPart, typeof(Button))]
public class GradientPreview : TemplatedControl
{
    private const string ButtonPart = "PART_Button";

    public static readonly StyledProperty<Gradient> GradientProperty =
        GradientBar.GradientProperty.AddOwner<GradientPreview>(new StyledPropertyMetadata<Gradient>(Gradient.White, BindingMode.TwoWay));

    private Button? _button;
    private GradientEditor? _editor;

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

    public Gradient Gradient
    {
        get => GetValue(GradientProperty);
        set => SetValue(GradientProperty, value);
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
                _editor = new GradientEditor { Gradient = Gradient };
                _editor.PropertyChanged += OnEditorPropertyChanged;
                _editor.AddHandler(ValueEdit.StartedEvent, ForwardEditEvent);
                _editor.AddHandler(ValueEdit.CompletedEvent, ForwardEditEvent);
                flyout.Content = _editor;
            }

            _editor.Gradient = Gradient;
        };
        _button.Flyout = flyout;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == GradientProperty && _editor is not null && !ReferenceEquals(_editor.Gradient, Gradient))
            _editor.Gradient = Gradient;
    }

    private void OnEditorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == GradientEditor.GradientProperty)
            SetCurrentValue(GradientProperty, _editor!.Gradient);
    }

    private void ForwardEditEvent(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        RaiseEvent(new RoutedEventArgs(e.RoutedEvent, this));
    }
}
