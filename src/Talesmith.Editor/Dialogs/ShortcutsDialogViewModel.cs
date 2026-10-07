using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Editor.Commands;
using Talesmith.UI.Services;

namespace Talesmith.Editor.Dialogs;

/// <summary>A single shortcut in the cheat sheet.</summary>
public sealed record ShortcutEntry(string Title, string Keys);

/// <summary>Shortcuts that share a category.</summary>
public sealed record ShortcutGroup(string Category, IReadOnlyList<ShortcutEntry> Entries);

/// <summary>A searchable cheat sheet of keyboard and mouse shortcuts.</summary>
public sealed partial class ShortcutsDialogViewModel : ObservableObject
{
    private static readonly ShortcutEntry[] MouseShortcuts =
    [
        new("Pan the view", "Middle Drag"),
        new("Pan the view", "Right Drag"),
        new("Pan the view", "Space+Drag"),
        new("Zoom around the pointer", "Wheel"),
        new("Pan horizontally", "Shift+Wheel"),
        new("Add to the selection", "Shift+Click"),
        new("Toggle in the selection", "Ctrl+Click"),
        new("Select an area", "Drag"),
        new("Move with grid snapping", "Ctrl+Drag")
    ];

    private readonly IDialogService _dialogs;
    private readonly IReadOnlyList<ShortcutGroup> _all;

    [ObservableProperty]
    private string _query = string.Empty;

    public ShortcutsDialogViewModel(IDialogService dialogs, EditorCommandRegistry commands)
    {
        _dialogs = dialogs;
        var groups = commands.Commands
            .Where(c => c.Gesture is not null || c.SharedGesture is not null)
            .GroupBy(c => c.Category)
            .Select(g => new ShortcutGroup(g.Key, [.. g.Select(c => new ShortcutEntry(c.Title, c.GestureText ?? KeyGestures.Format(c.SharedGesture!)))]))
            .ToList();
        groups.Add(new ShortcutGroup("Mouse", MouseShortcuts));
        _all = groups;
        Filter();
    }

    public ObservableCollection<ShortcutGroup> Groups { get; } = [];

    public bool HasResults => Groups.Count > 0;

    partial void OnQueryChanged(string value) => Filter();

    [RelayCommand]
    private void Close() => _dialogs.Close(this, null);

    private void Filter()
    {
        var terms = Query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Groups.Clear();
        foreach (var group in _all)
        {
            var entries = group.Entries
                .Where(e => terms.All(t => e.Title.Contains(t, StringComparison.OrdinalIgnoreCase) || e.Keys.Contains(t, StringComparison.OrdinalIgnoreCase)
                                           || group.Category.Contains(t, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (entries.Count > 0)
                Groups.Add(new ShortcutGroup(group.Category, entries));
        }

        OnPropertyChanged(nameof(HasResults));
    }
}
