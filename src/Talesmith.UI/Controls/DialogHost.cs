using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Talesmith.UI.Controls;

/// <summary>Hosts application content and presents modal dialogs above it.</summary>
[TemplatePart(LayersPartName, typeof(Panel))]
[PseudoClasses(":open")]
public class DialogHost : ContentControl
{
    private const string LayersPartName = "PART_Layers";
    private static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(160);

    private readonly List<DialogLayer> _layers = [];
    private Panel? _layersPanel;

    public DialogHost()
    {
        AddHandler(KeyDownEvent, OnKeyDownHandler, RoutingStrategies.Bubble);
    }

    /// <summary>Gets whether at least one dialog is open.</summary>
    public bool IsOpen => _layers.Count > 0;

    /// <summary>Returns the nearest <see cref="DialogHost"/> that contains <paramref name="visual"/>.</summary>
    public static DialogHost? Find(Visual visual)
    {
        ArgumentNullException.ThrowIfNull(visual);
        return visual as DialogHost ?? visual.FindAncestorOfType<DialogHost>();
    }

    /// <summary>Shows <paramref name="content"/> as a modal dialog and completes with the result passed to <see cref="Close"/>.</summary>
    public Task<object?> ShowAsync(object content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Dispatcher.UIThread.VerifyAccess();

        var layer = new DialogLayer(content)
        {
            PreviousFocus = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement(),
        };

        _layers.Add(layer);
        _layersPanel?.Children.Add(layer);
        PseudoClasses.Set(":open", true);

        Dispatcher.UIThread.Post(() =>
        {
            layer.IsShown = true;
            FocusInitialElement(layer);
        }, DispatcherPriority.Background);

        return layer.Completion.Task;
    }

    /// <summary>Closes the dialog showing <paramref name="content"/>, or the dialog containing it when it is a visual; closes the top dialog when null.</summary>
    public void Close(object? content, object? result)
    {
        var layer = content is null ? _layers.LastOrDefault() : FindLayer(content);
        if (layer is null)
        {
            return;
        }

        _layers.Remove(layer);
        PseudoClasses.Set(":open", _layers.Count > 0);
        layer.IsShown = false;
        layer.IsHitTestVisible = false;
        DispatcherTimer.RunOnce(() => _layersPanel?.Children.Remove(layer), ExitDuration);

        layer.PreviousFocus?.Focus();
        layer.Completion.TrySetResult(result);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _layersPanel = e.NameScope.Find<Panel>(LayersPartName);
        if (_layersPanel is null)
        {
            return;
        }

        foreach (var layer in _layers.Where(l => l.Parent is null))
        {
            _layersPanel.Children.Add(layer);
        }
    }

    private DialogLayer? FindLayer(object content)
    {
        var direct = _layers.LastOrDefault(l => ReferenceEquals(l.Content, content));
        if (direct is not null || content is not Visual visual)
        {
            return direct;
        }

        var ancestor = visual.FindAncestorOfType<DialogLayer>(includeSelf: true);
        return ancestor is not null && _layers.Contains(ancestor) ? ancestor : null;
    }

    private static void FocusInitialElement(DialogLayer layer)
    {
        var target = layer.GetVisualDescendants()
            .OfType<InputElement>()
            .FirstOrDefault(e => e is TextBox && e.Focusable && e.IsEffectivelyEnabled && e.IsEffectivelyVisible);

        (target ?? (InputElement)layer).Focus();
    }

    private void OnKeyDownHandler(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _layers.Count > 0)
        {
            Close(null, null);
            e.Handled = true;
        }
    }
}
