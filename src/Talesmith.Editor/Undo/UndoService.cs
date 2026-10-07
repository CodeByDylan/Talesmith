namespace Talesmith.Editor.Undo;

/// <summary>The default <see cref="IUndoService"/>, keeping up to <see cref="Limit"/> steps.</summary>
public sealed class UndoService(TimeProvider time) : IUndoService
{
    private readonly List<Entry> _undo = [];
    private readonly List<Entry> _redo = [];
    private readonly Dictionary<object, long> _saved = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _forcedDirty = new(ReferenceEqualityComparer.Instance);
    private CompositeCommand? _transaction;
    private string? _transactionDescription;
    private int _depth;
    private bool _cancelled;
    private bool _replaying;
    private long _nextStamp = 1;

    public UndoService()
        : this(TimeProvider.System)
    {
    }

    /// <summary>The most steps kept; the oldest are dropped first.</summary>
    public int Limit { get; set; } = 1000;

    public TimeSpan MergeWindow { get; set; } = TimeSpan.FromSeconds(1);

    public bool CanUndo => _undo.Count > 0 && _depth == 0;

    public bool CanRedo => _redo.Count > 0 && _depth == 0;

    public IReadOnlyList<IUndoableCommand> UndoSteps => [.. _undo.Select(e => e.Command)];

    public IReadOnlyList<IUndoableCommand> RedoSteps => [.. Enumerable.Reverse(_redo).Select(e => e.Command)];

    public bool IsInTransaction => _depth > 0;

    public event EventHandler? Changed;

    public void Execute(IUndoableCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        command.Apply();
        Record(command);
    }

    public void Record(IUndoableCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (_replaying)
            return;
        if (_transaction is not null)
        {
            _transaction.Add(command);
            return;
        }

        _redo.Clear();
        var now = time.GetUtcNow();
        if (_undo.Count > 0 && _undo[^1] is { Sealed: false } last && now - last.At <= MergeWindow && SameDocuments(last.Command, command)
            && last.Command.TryMerge(command))
        {
            _undo[^1] = last with { Stamp = _nextStamp++, At = now };
        }
        else
        {
            Push(new Entry(command, _nextStamp++, now, false));
        }

        RaiseChanged();
    }

    public void Undo()
    {
        if (!CanUndo)
            return;
        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Replay(entry.Command.Revert);
        _redo.Add(entry with { Sealed = true });
        RaiseChanged();
    }

    public void Redo()
    {
        if (!CanRedo)
            return;
        var entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        Replay(entry.Command.Apply);
        _undo.Add(entry);
        RaiseChanged();
    }

    public void MoveTo(int position)
    {
        position = Math.Clamp(position, 0, _undo.Count + _redo.Count);
        while (_undo.Count > position && CanUndo)
            Undo();
        while (_undo.Count < position && CanRedo)
            Redo();
    }

    public UndoTransaction BeginTransaction(string? description = null)
    {
        if (_depth++ == 0)
        {
            _transaction = new CompositeCommand(description ?? "", []);
            _transactionDescription = description;
            _cancelled = false;
        }

        return new UndoTransaction(description, EndTransaction);
    }

    public void Seal()
    {
        if (_undo.Count > 0)
            _undo[^1] = _undo[^1] with { Sealed = true };
    }

    public bool IsDirty(object document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return _forcedDirty.Contains(document) || CurrentStamp(document) != _saved.GetValueOrDefault(document);
    }

    public void MarkSaved(object document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _forcedDirty.Remove(document);
        _saved[document] = CurrentStamp(document);
        RaiseChanged();
    }

    public void MarkDirty(object document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_forcedDirty.Add(document))
            RaiseChanged();
    }

    public void Clear(object? document = null)
    {
        if (document is null)
        {
            _undo.Clear();
            _redo.Clear();
            _saved.Clear();
            _forcedDirty.Clear();
        }
        else
        {
            var dirty = IsDirty(document);
            _undo.RemoveAll(e => DocumentsOf(e.Command).Contains(document));
            _redo.RemoveAll(e => DocumentsOf(e.Command).Contains(document));
            _saved.Remove(document);
            _forcedDirty.Remove(document);
            if (dirty)
                _forcedDirty.Add(document);
        }

        RaiseChanged();
    }

    internal static IEnumerable<object> DocumentsOf(IUndoableCommand command) => command switch
    {
        CompositeCommand composite => composite.Documents,
        { Document: { } document } => [document],
        _ => []
    };

    private static bool SameDocuments(IUndoableCommand a, IUndoableCommand b) => ReferenceEquals(a.Document, b.Document);

    private void EndTransaction(UndoTransaction transaction, bool commit)
    {
        if (!commit)
            _cancelled = true;
        if (--_depth > 0)
            return;

        var composite = _transaction!;
        _transaction = null;
        if (_cancelled)
        {
            Replay(composite.Revert);
            return;
        }

        if (composite.Commands.Count == 0)
            return;
        var description = _transactionDescription ?? composite.Commands[0].Description;
        IUndoableCommand step = composite.Commands.Count == 1 && _transactionDescription is null ? composite.Commands[0] : new CompositeCommand(description, composite.Commands);
        _redo.Clear();
        Push(new Entry(step, _nextStamp++, time.GetUtcNow(), true));
        RaiseChanged();
    }

    private void Push(Entry entry)
    {
        _undo.Add(entry);
        if (_undo.Count > Limit)
            _undo.RemoveRange(0, _undo.Count - Limit);
    }

    private long CurrentStamp(object document)
    {
        for (var i = _undo.Count - 1; i >= 0; i--)
        {
            if (DocumentsOf(_undo[i].Command).Contains(document, ReferenceEqualityComparer.Instance))
                return _undo[i].Stamp;
        }

        return 0;
    }

    private void Replay(Action action)
    {
        _replaying = true;
        try
        {
            action();
        }
        finally
        {
            _replaying = false;
        }
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private sealed record Entry(IUndoableCommand Command, long Stamp, DateTimeOffset At, bool Sealed);
}
