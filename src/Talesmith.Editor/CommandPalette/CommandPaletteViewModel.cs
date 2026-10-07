using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Editor.Commands;
using Talesmith.Editor.Settings;
using Talesmith.UI.Services;

namespace Talesmith.Editor.CommandPalette;

/// <summary>A prefix that narrows the palette to one provider, shown as a hint under the search box.</summary>
public sealed record PaletteHint(string Prefix, string Category);

/// <summary>Searches commands and provider results from the keyboard, recently run items first.</summary>
public sealed partial class CommandPaletteViewModel : ObservableObject
{
    private const int ProviderLimit = 5;
    private const int MaxRecent = 30;

    private readonly IDialogService _dialogs;
    private readonly EditorCommandRegistry _commands;
    private readonly IReadOnlyList<ICommandPaletteProvider> _providers;
    private readonly ISettingsService _settings;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private PaletteItem? _selectedItem;

    public CommandPaletteViewModel(IDialogService dialogs, EditorCommandRegistry commands, IEnumerable<ICommandPaletteProvider> providers, ISettingsService settings)
    {
        _dialogs = dialogs;
        _commands = commands;
        _providers = [.. providers];
        _settings = settings;
        Hints = [.. _providers.Where(p => p.Prefix is not null).Select(p => new PaletteHint(p.Prefix!.Value.ToString(), p.Category))];
        Filter();
    }

    /// <summary>The results for the current query; replaced as a whole on every search.</summary>
    [ObservableProperty]
    private IReadOnlyList<PaletteItem> _results = [];

    public IReadOnlyList<PaletteHint> Hints { get; }

    public bool HasResults => Results.Count > 0;

    /// <summary>The placeholder text, naming the prefixes.</summary>
    public string Placeholder => Hints.Count == 0
        ? "Search commands…"
        : $"Search commands, or type {string.Join(", ", Hints.Select(h => $"{h.Prefix} for {h.Category.ToLowerInvariant()}"))}…";

    partial void OnQueryChanged(string value) => Filter();

    /// <summary>Moves the highlighted result by <paramref name="delta"/> rows, wrapping at the ends.</summary>
    public void MoveSelection(int delta)
    {
        if (Results.Count == 0)
            return;
        var index = SelectedItem is null ? -1 : IndexOf(SelectedItem);
        SelectedItem = Results[((index + delta) % Results.Count + Results.Count) % Results.Count];
    }

    [RelayCommand]
    private void Execute(PaletteItem? item)
    {
        item ??= SelectedItem;
        if (item is null || !item.IsEnabled)
            return;
        _dialogs.Close(this, null);
        if (item.Id is { } id)
        {
            _settings.Update(s =>
            {
                s.RecentCommands.Remove(id);
                s.RecentCommands.Insert(0, id);
                if (s.RecentCommands.Count > MaxRecent)
                    s.RecentCommands.RemoveRange(MaxRecent, s.RecentCommands.Count - MaxRecent);
            });
        }

        Dispatcher.UIThread.Post(item.Execute, DispatcherPriority.Input);
    }

    /// <summary>The results for the current query, best first.</summary>
    internal IReadOnlyList<PaletteItem> Search()
    {
        var recent = _settings.Current.RecentCommands;
        double Recency(PaletteItem item) => item.Id is { } id && recent.IndexOf(id) is >= 0 and var index ? Math.Max(0, 10 - index) : 0;

        var query = Query.Trim();
        if (query.Length > 0 && _providers.FirstOrDefault(p => p.Prefix == query[0]) is { } only)
        {
            var rest = query[1..].Trim();
            return [.. only.Search(rest, 100).OrderByDescending(i => i.Score + Recency(i))];
        }

        var commands = CommandItems(query).ToList();
        if (query.Length == 0)
        {
            return [.. commands
                .OrderByDescending(Recency)
                .ThenBy(i => Recency(i) > 0 ? string.Empty : i.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase)];
        }

        var results = new List<PaletteItem>(commands);
        foreach (var provider in _providers)
            results.AddRange(provider.Search(query, ProviderLimit).Take(ProviderLimit).Select(i => i with { Score = i.Score * 0.9 }));
        return [.. results.OrderByDescending(i => i.Score + Recency(i) * 0.6).ThenByDescending(i => i.IsEnabled)];
    }

    private IEnumerable<PaletteItem> CommandItems(string query)
    {
        foreach (var command in _commands.Commands)
        {
            if (!command.ShowInPalette)
                continue;
            var score = query.Length == 0 ? 1 : Math.Max(FuzzyMatch.Score(command.Title, query), FuzzyMatch.Score($"{command.Category} {command.Title}", query) * 0.8);
            if (score <= 0)
                continue;
            yield return new PaletteItem($"command:{command.Id}", command.Title, command.Category, () => command.TryExecute())
            {
                Icon = command.Icon,
                GestureText = command.GestureText,
                IsEnabled = command.CanExecute,
                Score = score
            };
        }
    }

    private int IndexOf(PaletteItem item)
    {
        for (var i = 0; i < Results.Count; i++)
        {
            if (ReferenceEquals(Results[i], item))
                return i;
        }

        return -1;
    }

    private void Filter()
    {
        Results = Search();
        SelectedItem = Results.FirstOrDefault(r => r.IsEnabled) ?? (Results.Count > 0 ? Results[0] : null);
        OnPropertyChanged(nameof(HasResults));
    }
}
