using Avalonia.Interactivity;
using Talesmith.UI.Controls;

namespace Talesmith.Editor.Undo;

/// <summary>Turns each interactive edit inside a control, bracketed by <see cref="ValueEdit"/> events (a drag on a number field, a color
/// picked in a flyout), into one undo step.</summary>
public static class UndoValueEdits
{
    /// <summary>Opens a transaction on every <see cref="ValueEdit.StartedEvent"/> below <paramref name="root"/> and finishes it on
    /// <see cref="ValueEdit.CompletedEvent"/>; dispose the result to stop.</summary>
    public static IDisposable Attach(Interactive root, IUndoService undo)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(undo);
        var open = new Stack<UndoTransaction>();
        EventHandler<RoutedEventArgs> started = (_, _) => open.Push(undo.BeginTransaction());
        EventHandler<RoutedEventArgs> completed = (_, _) =>
        {
            if (open.TryPop(out var transaction))
                transaction.Dispose();
        };
        ValueEdit.AddStartedHandler(root, started);
        ValueEdit.AddCompletedHandler(root, completed);
        return new Subscription(() =>
        {
            ValueEdit.RemoveStartedHandler(root, started);
            ValueEdit.RemoveCompletedHandler(root, completed);
            while (open.TryPop(out var transaction))
                transaction.Dispose();
        });
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
