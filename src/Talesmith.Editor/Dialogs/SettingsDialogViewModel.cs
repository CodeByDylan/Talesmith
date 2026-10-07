using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Editor.Commands;
using Talesmith.Editor.Settings;
using Talesmith.Runtime.Hosting;
using Talesmith.UI;
using Talesmith.UI.Services;
using Talesmith.UI.Theming;

namespace Talesmith.Editor.Dialogs;

/// <summary>A page in the settings dialog's sidebar.</summary>
public sealed record SettingsSection(string Title, Geometry Icon);

/// <summary>Edits editor preferences; every change is applied and saved immediately.</summary>
public sealed partial class SettingsDialogViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly ISettingsService _settings;
    private readonly IThemeManager _theme;
    private readonly EditorCommandRegistry _commands;
    private readonly List<KeyBindingViewModel> _bindings;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAppearanceSection), nameof(IsEditorSection), nameof(IsViewportSection), nameof(IsRenderingSection), nameof(IsKeyboardSection))]
    private int _selectedSectionIndex;

    [ObservableProperty]
    private string _bindingQuery = "";

    public SettingsDialogViewModel(IDialogService dialogs, ISettingsService settings, IThemeManager theme, EditorCommandRegistry commands)
    {
        _dialogs = dialogs;
        _settings = settings;
        _theme = theme;
        _commands = commands;
        _bindings = [.. commands.Commands.OrderBy(c => c.Category, StringComparer.Ordinal).ThenBy(c => c.Title, StringComparer.Ordinal).Select(c => new KeyBindingViewModel(c, this))];
        FilterBindings();
    }

    public IReadOnlyList<SettingsSection> Sections { get; } =
    [
        new("Appearance", Icons.Palette),
        new("Editor", Icons.PenLine),
        new("Viewport", Icons.Grid),
        new("Rendering", Icons.Cpu),
        new("Keyboard", Icons.Keyboard)
    ];

    public IReadOnlyList<Color> AccentPresets { get; } =
    [
        Color.Parse("#6366F1"), Color.Parse("#8B5CF6"), Color.Parse("#EC4899"), Color.Parse("#EF4444"), Color.Parse("#F97316"),
        Color.Parse("#EAB308"), Color.Parse("#22C55E"), Color.Parse("#14B8A6"), Color.Parse("#0EA5E9"), Color.Parse("#64748B")
    ];

    public IReadOnlyList<string> Renderers { get; } = ["Project setting", "Automatic", "Vulkan", "Skia"];

    public ObservableCollection<KeyBindingViewModel> Bindings { get; } = [];

    public bool IsAppearanceSection => SelectedSectionIndex == 0;

    public bool IsEditorSection => SelectedSectionIndex == 1;

    public bool IsViewportSection => SelectedSectionIndex == 2;

    public bool IsRenderingSection => SelectedSectionIndex == 3;

    public bool IsKeyboardSection => SelectedSectionIndex == 4;

    private EditorSettings Current => _settings.Current;

    public int ThemeModeIndex
    {
        get => (int)Current.Theme;
        set
        {
            var mode = (ThemeMode)Math.Clamp(value, 0, 2);
            _theme.Mode = mode;
            Apply(s => s.Theme = mode);
        }
    }

    public Color AccentColor
    {
        get => Color.TryParse(Current.AccentColor, out var color) ? color : AccentPresets[0];
        set
        {
            _theme.Accent = value;
            Apply(s => s.AccentColor = string.Create(CultureInfo.InvariantCulture, $"#{value.R:X2}{value.G:X2}{value.B:X2}"));
        }
    }

    public bool Autosave
    {
        get => Current.Autosave;
        set => Apply(s => s.Autosave = value);
    }

    public decimal? AutosaveMinutes
    {
        get => Current.AutosaveMinutes;
        set => Apply(s => s.AutosaveMinutes = (int)Math.Clamp(value ?? 5, 1, 120));
    }

    public string ExternalEditor
    {
        get => Current.ExternalEditor;
        set => Apply(s => s.ExternalEditor = value ?? "");
    }

    public bool ShowGrid
    {
        get => Current.ShowGrid;
        set => Apply(s => s.ShowGrid = value);
    }

    public bool ShowTileGrid
    {
        get => Current.ShowTileGrid;
        set => Apply(s => s.ShowTileGrid = value);
    }

    public bool SnapToGrid
    {
        get => Current.SnapToGrid;
        set => Apply(s => s.SnapToGrid = value);
    }

    public decimal? GridSize
    {
        get => (decimal)Current.GridSize;
        set => Apply(s => s.GridSize = Math.Clamp((double)(value ?? 32), 1, 4096));
    }

    public decimal? RotationSnap
    {
        get => (decimal)Current.RotationSnapDegrees;
        set => Apply(s => s.RotationSnapDegrees = Math.Clamp((double)(value ?? 15), 1, 180));
    }

    public bool ShowEntityIcons
    {
        get => Current.ShowEntityIcons;
        set => Apply(s => s.ShowEntityIcons = value);
    }

    public bool SmoothZoom
    {
        get => Current.SmoothZoom;
        set => Apply(s => s.SmoothZoom = value);
    }

    public decimal? ZoomStep
    {
        get => (decimal)Current.ZoomStep;
        set => Apply(s => s.ZoomStep = Math.Clamp((double)(value ?? 1.2m), 1.05, 2.0));
    }

    public int RendererIndex
    {
        get => Current.Renderer is { } renderer ? (int)renderer + 1 : 0;
        set => Apply(s => s.Renderer = value <= 0 ? null : (RendererPreference)Math.Clamp(value - 1, 0, 2));
    }

    partial void OnBindingQueryChanged(string value) => FilterBindings();

    [RelayCommand]
    private void ResetBindings()
    {
        Apply(s => s.KeyBindings.Clear());
        Refresh();
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        var defaults = new EditorSettings();
        _settings.Update(s =>
        {
            s.Theme = defaults.Theme;
            s.AccentColor = defaults.AccentColor;
            s.Autosave = defaults.Autosave;
            s.AutosaveMinutes = defaults.AutosaveMinutes;
            s.ExternalEditor = defaults.ExternalEditor;
            s.ShowGrid = defaults.ShowGrid;
            s.ShowTileGrid = defaults.ShowTileGrid;
            s.SnapToGrid = defaults.SnapToGrid;
            s.GridSize = defaults.GridSize;
            s.RotationSnapDegrees = defaults.RotationSnapDegrees;
            s.ShowEntityIcons = defaults.ShowEntityIcons;
            s.SmoothZoom = defaults.SmoothZoom;
            s.ZoomStep = defaults.ZoomStep;
            s.Renderer = defaults.Renderer;
            s.KeyBindings.Clear();
        });
        _theme.Mode = defaults.Theme;
        _theme.Accent = Color.Parse(defaults.AccentColor);
        Refresh();
    }

    [RelayCommand]
    private void Close() => _dialogs.Close(this, null);

    internal void SetBinding(EditorCommand command, string? gesture)
    {
        Apply(s =>
        {
            if (gesture is null)
                s.KeyBindings.Remove(command.Id);
            else
                s.KeyBindings[command.Id] = gesture;
        });
        foreach (var binding in _bindings)
            binding.Refresh();
    }

    internal EditorCommand Effective(string id) => _commands.Find(id)!;

    internal string? ConflictOf(EditorCommand command)
    {
        if (command.Gesture is not { } gesture)
            return null;
        var other = _commands.Commands.FirstOrDefault(c => c.Id != command.Id && c.Gesture == gesture);
        return other is null ? null : $"Also used by {other.Title}";
    }

    private void Refresh()
    {
        OnPropertyChanged(string.Empty);
        foreach (var binding in _bindings)
            binding.Refresh();
    }

    private void FilterBindings()
    {
        var terms = BindingQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Bindings.Clear();
        foreach (var binding in _bindings.Where(b => terms.All(t => b.Title.Contains(t, StringComparison.OrdinalIgnoreCase)
                                                                   || b.Category.Contains(t, StringComparison.OrdinalIgnoreCase)
                                                                   || (b.GestureText?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false))))
            Bindings.Add(binding);
    }

    private void Apply(Action<EditorSettings> change, [CallerMemberName] string? propertyName = null)
    {
        _settings.Update(change);
        OnPropertyChanged(propertyName);
    }
}

/// <summary>A command's shortcut in the keyboard settings; pressing keys while it is being recorded sets a new shortcut.</summary>
public sealed partial class KeyBindingViewModel(EditorCommand command, SettingsDialogViewModel owner) : ObservableObject
{
    [ObservableProperty]
    private bool _isRecording;

    public string Id => command.Id;

    public string Title => command.Title;

    public string Category => command.Category;

    public Geometry? Icon => command.Icon;

    public string? GestureText => owner.Effective(command.Id).GestureText;

    public bool HasGesture => GestureText is not null;

    public string DisplayText => IsRecording ? "Press keys…" : GestureText ?? "None";

    public bool IsOverridden => owner.Effective(command.Id).Gesture != command.DefaultGesture;

    public string? Conflict => owner.ConflictOf(owner.Effective(command.Id));

    public bool HasConflict => Conflict is not null;

    [RelayCommand]
    private void Record() => IsRecording = true;

    [RelayCommand]
    private void Reset()
    {
        IsRecording = false;
        owner.SetBinding(command, null);
    }

    [RelayCommand]
    private void Clear()
    {
        IsRecording = false;
        owner.SetBinding(command, "");
    }

    /// <summary>Records a key press as the new shortcut; returns false for modifier keys alone. Escape cancels.</summary>
    public bool Capture(Key key, KeyModifiers modifiers)
    {
        if (!IsRecording || key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            return false;
        IsRecording = false;
        if (key == Key.Escape && modifiers == KeyModifiers.None)
            return true;
        owner.SetBinding(command, new KeyGesture(key, modifiers).ToString());
        return true;
    }

    internal void Refresh()
    {
        OnPropertyChanged(nameof(GestureText));
        OnPropertyChanged(nameof(HasGesture));
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(IsOverridden));
        OnPropertyChanged(nameof(Conflict));
        OnPropertyChanged(nameof(HasConflict));
    }

    partial void OnIsRecordingChanged(bool value) => OnPropertyChanged(nameof(DisplayText));
}
