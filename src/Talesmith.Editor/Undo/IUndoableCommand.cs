namespace Talesmith.Editor.Undo;

/// <summary>An edit that can be undone and redone, storing only what it changes.</summary>
public interface IUndoableCommand
{
    /// <summary>What the edit did, shown in the History panel and the Edit menu, such as "Move Hero".</summary>
    string Description { get; }

    /// <summary>The document the edit changes, whose unsaved state it affects, or null for edits outside documents.</summary>
    object? Document { get; }

    void Apply();

    void Revert();

    /// <summary>Folds a following edit into this one, such as another change of the same property; returns false when they cannot merge.</summary>
    /// <remarks>Only called for edits that follow each other closely; see <see cref="IUndoService.MergeWindow"/>.</remarks>
    bool TryMerge(IUndoableCommand next) => false;
}

/// <summary>An undoable edit made from two delegates.</summary>
public sealed class DelegateCommand(string description, Action apply, Action revert, object? document = null) : IUndoableCommand
{
    public string Description { get; } = description;

    public object? Document { get; } = document;

    public void Apply() => apply();

    public void Revert() => revert();
}
