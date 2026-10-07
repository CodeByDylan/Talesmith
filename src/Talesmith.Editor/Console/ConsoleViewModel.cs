using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.UI;

namespace Talesmith.Editor.Console;

/// <summary>A row of the console: one entry, or several identical entries collapsed into one with a count.</summary>
public sealed partial class ConsoleRow(ConsoleEntry entry) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText), nameof(HasCount))]
    private int _count = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeText))]
    private ConsoleEntry _latest = entry;

    public ConsoleEntry Entry { get; } = entry;

    public ConsoleSeverity Severity => Entry.Severity;

    public string Message { get; } = FirstLine(entry.Message);

    public string FullMessage => Entry.Details is { } details ? $"{Entry.Message}{Environment.NewLine}{Environment.NewLine}{details}" : Entry.Message;

    public string SourceText => Entry.Source.ToString();

    public string TimeText => Latest.Time.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);

    public string CountText => Count > 999 ? "999+" : Count.ToString(CultureInfo.CurrentCulture);

    public bool HasCount => Count > 1;

    public bool HasTarget => Entry.Target is not null;

    public bool HasDetails => Entry.Details is not null || Entry.Message.Contains('\n', StringComparison.Ordinal);

    public Geometry Icon => Severity switch
    {
        ConsoleSeverity.Error => Icons.AlertCircle,
        ConsoleSeverity.Warning => Icons.AlertTriangle,
        ConsoleSeverity.Debug => Icons.Bug,
        _ => Icons.Info
    };

    public bool IsError => Severity == ConsoleSeverity.Error;

    public bool IsWarning => Severity == ConsoleSeverity.Warning;

    public bool IsInfo => Severity is ConsoleSeverity.Info or ConsoleSeverity.Debug;

    internal (ConsoleSeverity, ConsoleSource, string) Key => (Entry.Severity, Entry.Source, Entry.Message);

    private static string FirstLine(string text)
    {
        var newline = text.IndexOf('\n', StringComparison.Ordinal);
        return (newline < 0 ? text : text[..newline]).TrimEnd('\r');
    }
}

/// <summary>The console panel: filters by severity, source and text, collapses repeated messages and opens what entries refer to.</summary>
public sealed partial class ConsoleViewModel : ObservableObject
{
    private const int MaxRows = 20_000;

    private readonly IConsole _console;
    private readonly ConsoleNavigator _navigator;
    private readonly Dictionary<(ConsoleSeverity, ConsoleSource, string), ConsoleRow> _collapsed = [];
    private readonly HashSet<ConsoleEntry> _shown = new(ReferenceEqualityComparer.Instance);
    private int _infoCount;
    private int _warningCount;
    private int _errorCount;

    [ObservableProperty]
    private bool _showInfo = true;

    [ObservableProperty]
    private bool _showWarnings = true;

    [ObservableProperty]
    private bool _showErrors = true;

    [ObservableProperty]
    private bool _collapse = true;

    [ObservableProperty]
    private int _sourceIndex;

    [ObservableProperty]
    private string _search = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedDetails))]
    private ConsoleRow? _selectedRow;

    public ConsoleViewModel(IConsole console, ConsoleNavigator navigator)
    {
        _console = console;
        _navigator = navigator;
        _console.EntriesAdded += (_, e) => Append(e.Entries);
        _console.Cleared += (_, _) => Rebuild();
        Rebuild();
    }

    public ObservableCollection<ConsoleRow> Rows { get; } = [];

    public IReadOnlyList<string> Sources { get; } = ["All sources", .. Enum.GetNames<ConsoleSource>()];

    public string InfoCount => Format(_infoCount);

    public string WarningCount => Format(_warningCount);

    public string ErrorCount => Format(_errorCount);

    public bool IsEmpty => Rows.Count == 0;

    public bool HasSelectedDetails => SelectedRow?.HasDetails == true;

    /// <summary>Raised after rows were appended, so the view can keep the newest row in sight.</summary>
    public event EventHandler? RowsAppended;

    [RelayCommand]
    private void Clear() => _console.Clear();

    [RelayCommand]
    private async Task OpenAsync(ConsoleRow? row)
    {
        if ((row ?? SelectedRow)?.Entry.Target is { } target)
            await _navigator.NavigateAsync(target);
    }

    partial void OnShowInfoChanged(bool value) => Rebuild();

    partial void OnShowWarningsChanged(bool value) => Rebuild();

    partial void OnShowErrorsChanged(bool value) => Rebuild();

    partial void OnCollapseChanged(bool value) => Rebuild();

    partial void OnSourceIndexChanged(int value) => Rebuild();

    partial void OnSearchChanged(string value) => Rebuild();

    private void Rebuild()
    {
        Rows.Clear();
        _collapsed.Clear();
        _infoCount = _warningCount = _errorCount = 0;
        var entries = _console.Entries;
        _shown.Clear();
        _shown.UnionWith(entries);
        Add(entries);
        RaiseCounts();
    }

    private void Append(IReadOnlyList<ConsoleEntry> entries)
    {
        if (_shown.Count > 0)
        {
            entries = [.. entries.Where(e => !_shown.Contains(e))];
            _shown.Clear();
        }

        Add(entries);
        TrimRows();
        RaiseCounts();
        RowsAppended?.Invoke(this, EventArgs.Empty);
    }

    private void TrimRows()
    {
        if (Rows.Count <= MaxRows + MaxRows / 8)
            return;
        while (Rows.Count > MaxRows)
        {
            var row = Rows[0];
            if (_collapsed.TryGetValue(row.Key, out var collapsed) && collapsed == row)
                _collapsed.Remove(row.Key);
            Rows.RemoveAt(0);
        }
    }

    private void Add(IEnumerable<ConsoleEntry> entries)
    {
        foreach (var entry in entries)
        {
            switch (entry.Severity)
            {
                case ConsoleSeverity.Error:
                    _errorCount++;
                    break;
                case ConsoleSeverity.Warning:
                    _warningCount++;
                    break;
                default:
                    _infoCount++;
                    break;
            }

            if (!Matches(entry))
                continue;
            if (Collapse && _collapsed.TryGetValue((entry.Severity, entry.Source, entry.Message), out var row))
            {
                row.Count++;
                row.Latest = entry;
                continue;
            }

            row = new ConsoleRow(entry);
            if (Collapse)
                _collapsed[row.Key] = row;
            Rows.Add(row);
        }
    }

    private bool Matches(ConsoleEntry entry)
    {
        var severity = entry.Severity switch
        {
            ConsoleSeverity.Error => ShowErrors,
            ConsoleSeverity.Warning => ShowWarnings,
            _ => ShowInfo
        };
        if (!severity)
            return false;
        if (SourceIndex > 0 && (int)entry.Source != SourceIndex - 1)
            return false;
        return Search.Length == 0 || entry.Message.Contains(Search, StringComparison.OrdinalIgnoreCase)
                                  || (entry.Category?.Contains(Search, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(InfoCount));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private static string Format(int count) => count > 999 ? "999+" : count.ToString(CultureInfo.CurrentCulture);
}
