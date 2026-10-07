namespace Talesmith.Editor.Undo;

/// <summary>The editor's undo history: records edits, groups them into transactions, merges rapid edits and tracks unsaved documents.</summary>
/// <remarks>
/// <para>Make every change through <see cref="Execute"/>, which applies the edit and records it. Consecutive edits that
/// <see cref="IUndoableCommand.TryMerge"/> accepts become one step when they follow within <see cref="MergeWindow"/>.</para>
/// <para>Group edits with <see cref="BeginTransaction"/>: <c>using var tx = undo.BeginTransaction("Paint tiles");</c>. Everything executed
/// until the transaction is disposed becomes one step; nested transactions join the outermost one. Interactive edits bracketed by
/// <c>ValueEdit.Started</c> and <c>Completed</c> should run in a transaction, which <see cref="UndoValueEdits"/> does for a whole panel.</para>
/// </remarks>
public interface IUndoService
{
    bool CanUndo { get; }

    bool CanRedo { get; }

    /// <summary>The steps that can be undone, oldest first.</summary>
    IReadOnlyList<IUndoableCommand> UndoSteps { get; }

    /// <summary>The steps that can be redone, the next one first.</summary>
    IReadOnlyList<IUndoableCommand> RedoSteps { get; }

    /// <summary>How long after an edit a following edit may still merge into it.</summary>
    TimeSpan MergeWindow { get; set; }

    /// <summary>Whether a transaction is open.</summary>
    bool IsInTransaction { get; }

    /// <summary>Raised after the history or a document's unsaved state changed.</summary>
    event EventHandler? Changed;

    /// <summary>Applies an edit and records it.</summary>
    void Execute(IUndoableCommand command);

    /// <summary>Records an edit that was already applied.</summary>
    void Record(IUndoableCommand command);

    void Undo();

    void Redo();

    /// <summary>Undoes or redoes until <paramref name="position"/> steps are applied.</summary>
    void MoveTo(int position);

    /// <summary>Starts grouping edits into one step; dispose the transaction to finish it.</summary>
    /// <param name="description">The step's description; null uses the first edit's.</param>
    UndoTransaction BeginTransaction(string? description = null);

    /// <summary>Stops the last step from merging with the next edit, such as when a drag ends.</summary>
    void Seal();

    /// <summary>Whether <paramref name="document"/> changed since <see cref="MarkSaved"/>.</summary>
    bool IsDirty(object document);

    /// <summary>Marks the current state of <paramref name="document"/> as saved.</summary>
    void MarkSaved(object document);

    /// <summary>Marks <paramref name="document"/> as changed by something outside the history.</summary>
    void MarkDirty(object document);

    /// <summary>Removes the steps of one document, such as when it closes, or every step when <paramref name="document"/> is null.</summary>
    void Clear(object? document = null);
}
