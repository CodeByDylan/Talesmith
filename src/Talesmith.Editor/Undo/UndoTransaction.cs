namespace Talesmith.Editor.Undo;

/// <summary>Groups the edits executed while it is open into one undo step; see <see cref="IUndoService.BeginTransaction"/>.</summary>
public sealed class UndoTransaction : IDisposable
{
    private readonly Action<UndoTransaction, bool> _end;
    private bool _ended;

    internal UndoTransaction(string? description, Action<UndoTransaction, bool> end)
    {
        Description = description;
        _end = end;
    }

    public string? Description { get; }

    /// <summary>Reverts the edits made in this transaction and records nothing; nested transactions cancel the outermost one.</summary>
    public void Cancel()
    {
        if (_ended)
            return;
        _ended = true;
        _end(this, false);
    }

    /// <summary>Finishes the transaction, recording its edits as one step.</summary>
    public void Dispose()
    {
        if (_ended)
            return;
        _ended = true;
        _end(this, true);
    }
}
