namespace Talesmith.Editor.Undo;

/// <summary>Several edits undone and redone as one step, such as the edits of a transaction.</summary>
public sealed class CompositeCommand : IUndoableCommand
{
    private readonly List<IUndoableCommand> _commands;

    public CompositeCommand(string description, IEnumerable<IUndoableCommand> commands)
    {
        Description = description;
        _commands = [.. commands];
    }

    public string Description { get; }

    public IReadOnlyList<IUndoableCommand> Commands => _commands;

    /// <summary>The document every edit changes, or null when they change different documents or none.</summary>
    public object? Document
    {
        get
        {
            var documents = Documents;
            return documents.Count == 1 ? documents[0] : null;
        }
    }

    /// <summary>Every document the edits change.</summary>
    public IReadOnlyList<object> Documents => [.. _commands.SelectMany(UndoService.DocumentsOf).Distinct(ReferenceEqualityComparer.Instance)];

    public void Apply()
    {
        foreach (var command in _commands)
            command.Apply();
    }

    public void Revert()
    {
        for (var i = _commands.Count - 1; i >= 0; i--)
            _commands[i].Revert();
    }

    /// <summary>Adds an edit, merging it into the last one when that accepts it.</summary>
    internal void Add(IUndoableCommand command)
    {
        if (_commands.Count > 0 && _commands[^1].TryMerge(command))
            return;
        _commands.Add(command);
    }
}
