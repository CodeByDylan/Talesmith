using System.Collections.ObjectModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Panels;

namespace Talesmith.Editor.Undo;

/// <summary>A step in the History panel.</summary>
/// <param name="Position">The number of undo steps that exist when this entry is current.</param>
public sealed record HistoryEntryViewModel(int Position, string Description, bool IsUndone, bool IsCurrent);

/// <summary>The History panel: lists undo and redo steps and jumps between them.</summary>
public sealed partial class HistoryPanel : ObservableObject, IEditorPanel
{
    private readonly IUndoService _undo;
    private readonly ISceneDocumentService _documents;

    public HistoryPanel(IUndoService undo, ISceneDocumentService documents)
    {
        _undo = undo;
        _documents = documents;
        undo.Changed += (_, _) => Rebuild();
        documents.ActiveChanged += (_, _) => Rebuild();
        Rebuild();
    }

    public ObservableCollection<HistoryEntryViewModel> Entries { get; } = [];

    public string Summary => $"{_undo.UndoSteps.Count} undo · {_undo.RedoSteps.Count} redo";

    public Control CreateContent() => new HistoryView { DataContext = this };

    [RelayCommand]
    private void JumpTo(HistoryEntryViewModel? entry)
    {
        if (entry is not null)
            _undo.MoveTo(entry.Position);
    }

    private void Rebuild()
    {
        var undo = _undo.UndoSteps;
        var redo = _undo.RedoSteps;
        Entries.Clear();
        Entries.Add(new HistoryEntryViewModel(0, _documents.Active is { } model ? $"Opened {model.FileName}" : "Start", false, undo.Count == 0));
        for (var i = 0; i < undo.Count; i++)
            Entries.Add(new HistoryEntryViewModel(i + 1, undo[i].Description, false, i == undo.Count - 1));
        for (var i = 0; i < redo.Count; i++)
            Entries.Add(new HistoryEntryViewModel(undo.Count + i + 1, redo[i].Description, true, false));
        OnPropertyChanged(nameof(Summary));
    }
}
