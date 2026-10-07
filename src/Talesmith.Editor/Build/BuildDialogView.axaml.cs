using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Talesmith.Editor.Build;

public partial class BuildDialogView : UserControl
{
    private Border? _dragged;
    private int _dragIndex = -1;

    public BuildDialogView()
    {
        InitializeComponent();
        SceneList.AddHandler(PointerPressedEvent, OnScenePointerPressed, handledEventsToo: true);
        SceneList.AddHandler(PointerMovedEvent, OnScenePointerMoved, handledEventsToo: true);
        SceneList.AddHandler(PointerReleasedEvent, OnScenePointerReleased, handledEventsToo: true);
        SceneList.AddHandler(PointerCaptureLostEvent, (_, _) => EndDrag(), handledEventsToo: true);
    }

    private BuildDialogViewModel? ViewModel => DataContext as BuildDialogViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (ViewModel is { } model)
            model.VisibleLog.CollectionChanged += OnLogChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (ViewModel is { } model)
        {
            model.VisibleLog.CollectionChanged -= OnLogChanged;
            model.Dispose();
        }
    }

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || ViewModel is not { } model || model.VisibleLog.Count == 0)
            return;
        Dispatcher.UIThread.Post(() => LogList.ScrollIntoView(model.VisibleLog.Count - 1), DispatcherPriority.Background);
    }

    private void OnScenePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Visual source || source.FindAncestorOfType<Border>(includeSelf: true) is not { } hit)
            return;
        var grip = hit.Classes.Contains("grip") ? hit : hit.FindAncestorOfType<Border>() is { } parent && parent.Classes.Contains("grip") ? parent : null;
        if (grip?.FindAncestorOfType<ContentPresenter>() is not { } container || !e.GetCurrentPoint(SceneList).Properties.IsLeftButtonPressed)
            return;
        _dragIndex = SceneList.IndexFromContainer(container);
        if (_dragIndex < 0)
            return;
        _dragged = container.FindDescendantOfType<Border>();
        _dragged?.Classes.Add("dragging");
        e.Pointer.Capture(SceneList);
        e.Handled = true;
    }

    private void OnScenePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragIndex < 0 || ViewModel is not { } model)
            return;
        var y = e.GetPosition(SceneList).Y;
        for (var i = 0; i < model.Scenes.Count; i++)
        {
            if (SceneList.ContainerFromIndex(i) is not { } container)
                continue;
            var top = container.TranslatePoint(new Point(0, 0), SceneList)?.Y ?? 0;
            if (y < top || y > top + container.Bounds.Height || i == _dragIndex)
                continue;
            model.MoveScene(_dragIndex, i);
            _dragIndex = i;
            _dragged?.Classes.Remove("dragging");
            _dragged = SceneList.ContainerFromIndex(i)?.FindDescendantOfType<Border>();
            _dragged?.Classes.Add("dragging");
            break;
        }
    }

    private void OnScenePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragIndex < 0)
            return;
        e.Pointer.Capture(null);
        EndDrag();
    }

    private void EndDrag()
    {
        _dragged?.Classes.Remove("dragging");
        _dragged = null;
        _dragIndex = -1;
    }
}
