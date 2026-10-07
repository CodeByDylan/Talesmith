using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace Talesmith.UI.Controls;

/// <summary>A modal layer inside <see cref="DialogHost"/> that dims the content beneath and centers one dialog, no larger than the space the
/// layer leaves around it.</summary>
[TemplatePart(ContentPresenterPart, typeof(ContentPresenter))]
[PseudoClasses(":shown")]
public class DialogLayer : ContentControl
{
    /// <summary>The largest size a dialog in the layer can take, inherited by the <see cref="Dialog"/> wherever it is in the layer's content.</summary>
    internal static readonly AttachedProperty<Size> RoomProperty =
        AvaloniaProperty.RegisterAttached<DialogLayer, Control, Size>("Room", new Size(double.PositiveInfinity, double.PositiveInfinity), inherits: true);

    private const string ContentPresenterPart = "PART_ContentPresenter";

    private ContentPresenter? _presenter;

    internal DialogLayer(object content)
    {
        Content = content;
        Focusable = true;
    }

    internal TaskCompletionSource<object?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal IInputElement? PreviousFocus { get; init; }

    internal bool IsShown
    {
        get => PseudoClasses.Contains(":shown");
        set => PseudoClasses.Set(":shown", value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _presenter = e.NameScope.Find<ContentPresenter>(ContentPresenterPart);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var margin = _presenter?.Margin ?? default;
        SetValue(RoomProperty, new Size(
            Math.Max(0, availableSize.Width - margin.Left - margin.Right),
            Math.Max(0, availableSize.Height - margin.Top - margin.Bottom)));
        return base.MeasureOverride(availableSize);
    }
}
