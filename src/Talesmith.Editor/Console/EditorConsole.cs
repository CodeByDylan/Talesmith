using Avalonia.Threading;

namespace Talesmith.Editor.Console;

/// <summary>The default <see cref="IConsole"/>: keeps the newest <see cref="Capacity"/> entries and reports new ones to the UI in batches.</summary>
public sealed class EditorConsole : IConsole
{
    private readonly Lock _lock = new();
    private readonly List<ConsoleEntry> _entries = [];
    private readonly List<ConsoleEntry> _pending = [];
    private bool _flushQueued;

    public int Capacity { get; init; } = 20_000;

    public IReadOnlyList<ConsoleEntry> Entries
    {
        get
        {
            lock (_lock)
                return _entries.Count > Capacity ? _entries.GetRange(_entries.Count - Capacity, Capacity) : [.. _entries];
        }
    }

    public event EventHandler<ConsoleEntriesEventArgs>? EntriesAdded;

    public event EventHandler? Cleared;

    public void Write(ConsoleEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_lock)
        {
            _entries.Add(entry);
            if (_entries.Count > Capacity + Capacity / 8)
                _entries.RemoveRange(0, _entries.Count - Capacity);
            _pending.Add(entry);
            if (_pending.Count > Capacity + Capacity / 8)
                _pending.RemoveRange(0, _pending.Count - Capacity);
            if (_flushQueued)
                return;
            _flushQueued = true;
        }

        Dispatcher.UIThread.Post(Flush, DispatcherPriority.Background);
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            _pending.Clear();
        }

        if (Dispatcher.UIThread.CheckAccess())
            Cleared?.Invoke(this, EventArgs.Empty);
        else
            Dispatcher.UIThread.Post(() => Cleared?.Invoke(this, EventArgs.Empty));
    }

    private void Flush()
    {
        ConsoleEntry[] batch;
        lock (_lock)
        {
            batch = [.. _pending];
            _pending.Clear();
            _flushQueued = false;
        }

        if (batch.Length > 0)
            EntriesAdded?.Invoke(this, new ConsoleEntriesEventArgs(batch));
    }
}
