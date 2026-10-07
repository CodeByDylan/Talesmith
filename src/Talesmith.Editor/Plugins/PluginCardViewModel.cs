using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Plugins;

namespace Talesmith.Editor.Plugins;

/// <summary>A permission on a plugin card.</summary>
/// <param name="IsUndeclared">Whether the plugin uses it without declaring it.</param>
public sealed record PluginPermissionChip(string Name, string Title, string Description, bool IsUndeclared)
{
    public string ToolTip => IsUndeclared ? $"{Title}: used but not declared in plugin.json. {Description}" : $"{Title}: {Description}";
}

/// <summary>A dependency on a plugin card, with whether it is met.</summary>
public sealed record PluginDependencyRow(string Id, string Range, bool Optional, string Status, bool IsMet)
{
    public string Requirement => Optional ? $"{Range} · optional" : Range;
}

/// <summary>A key and its JSON value in a plugin's settings.</summary>
public sealed record PluginSettingRow(string Key, string Value);

/// <summary>One installed plugin in the Plugins panel.</summary>
public sealed partial class PluginCardViewModel : ObservableObject
{
    private static readonly IReadOnlyDictionary<PluginPermissions, string> PermissionDescriptions = new Dictionary<PluginPermissions, string>
    {
        [PluginPermissions.FileSystem] = "Reads or writes files directly instead of through the asset manager.",
        [PluginPermissions.Network] = "Uses the network.",
        [PluginPermissions.ProcessExecution] = "Starts other programs.",
        [PluginPermissions.EditorUi] = "Extends the editor with panels, tools or commands.",
        [PluginPermissions.RuntimeScene] = "Adds systems, scenes and scene listeners to games.",
        [PluginPermissions.AssetWrite] = "Creates, changes or deletes asset files.",
        [PluginPermissions.RenderBackend] = "Replaces the renderer, adds render passes or uses Vulkan or Skia directly."
    };

    private readonly Func<PluginCardViewModel, bool, Task> _setEnabled;
    private bool _updating;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _changePending;

    public PluginCardViewModel(PluginLoadEntry current, PluginLoadEntry next, PluginLoadReport installed, PluginConfiguration configuration,
        IReadOnlyList<EditorPluginFault> editorFaults, Func<PluginCardViewModel, bool, Task> setEnabled)
    {
        ArgumentNullException.ThrowIfNull(editorFaults);
        _setEnabled = setEnabled;
        var manifest = current.Manifest ?? next.Manifest;
        Id = manifest?.Id ?? Path.GetFileName(current.Directory);
        Name = current.DisplayName;
        Directory = current.Directory;
        Version = manifest?.Version.ToString(3) ?? "";
        Authors = manifest is { Authors.Count: > 0 } ? string.Join(", ", manifest.Authors) : "";
        Description = manifest?.Description is { Length: > 0 } description ? description : "No description.";
        License = manifest?.License;
        Homepage = manifest?.Homepage;
        State = current.State;
        Reason = current.Reason;
        EditorErrors = [.. editorFaults.Select(f => $"Failed to {f.Action}: {f.Message}")];
        ErrorDetails = current.ErrorDetails ?? (editorFaults.Count > 0 ? string.Join(Environment.NewLine + Environment.NewLine, editorFaults.Select(f => f.Details)) : null);
        CanToggle = manifest is not null;
        _isEnabled = next.State != PluginState.Disabled;
        Warnings = current.Warnings;
        Extensions = manifest?.Extensions ?? [];
        Permissions =
        [
            .. PluginPermissionNames.Split(current.Permissions).Select(p => Chip(p, undeclared: false)),
            .. PluginPermissionNames.Split(current.UndeclaredPermissions).Select(p => Chip(p, undeclared: true))
        ];
        Dependencies = [.. (manifest?.Dependencies ?? []).Select(d => Dependency(d, installed))];
        Settings = configuration.Settings.TryGetValue(Id, out var settings) && settings.ValueKind == JsonValueKind.Object
            ? [.. settings.EnumerateObject().Select(p => new PluginSettingRow(p.Name, p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : p.Value.GetRawText()))]
            : [];
        Icon = LoadIcon(current.Directory, manifest?.IconFile);
        Monogram = Name.Length > 0 ? char.ToUpper(Name[0], CultureInfo.CurrentCulture).ToString() : "?";
        ChangePending = (current.State == PluginState.Disabled) != (next.State == PluginState.Disabled);
    }

    public string Id { get; }

    public string Name { get; }

    public string Directory { get; }

    public string Version { get; }

    public string Authors { get; }

    public bool HasAuthors => Authors.Length > 0;

    public string Description { get; }

    public string? License { get; }

    public Uri? Homepage { get; }

    public bool HasHomepage => Homepage is not null;

    public PluginState State { get; }

    public string StateText => State switch
    {
        PluginState.Loaded when HasEditorErrors => "Editor errors",
        PluginState.Loaded => "Loaded",
        PluginState.Disabled => "Disabled",
        PluginState.Skipped => "Skipped",
        PluginState.Failed => "Failed",
        _ => "Not loaded"
    };

    public bool IsLoaded => State == PluginState.Loaded;

    public bool IsFailed => State == PluginState.Failed;

    public bool IsSkipped => State == PluginState.Skipped;

    public bool IsDisabled => State is PluginState.Disabled or PluginState.Pending;

    /// <summary>What the plugin's editor code failed to do since the project opened; the editor skips those parts.</summary>
    public IReadOnlyList<string> EditorErrors { get; }

    public bool HasEditorErrors => EditorErrors.Count > 0;

    /// <summary>Whether the plugin did not load or its editor code failed.</summary>
    public bool HasErrors => IsFailed || HasEditorErrors;

    public bool IsHealthy => IsLoaded && !HasEditorErrors;

    /// <summary>Why the plugin did not load, or null.</summary>
    public string? Reason { get; }

    public bool HasReason => !string.IsNullOrEmpty(Reason);

    public string? ErrorDetails { get; }

    public bool HasErrorDetails => !string.IsNullOrEmpty(ErrorDetails);

    public bool CanToggle { get; }

    public IReadOnlyList<string> Warnings { get; }

    public bool HasWarnings => Warnings.Count > 0;

    public IReadOnlyList<PluginPermissionChip> Permissions { get; }

    public bool HasPermissions => Permissions.Count > 0;

    public IReadOnlyList<string> Extensions { get; }

    public bool HasExtensions => Extensions.Count > 0;

    public IReadOnlyList<PluginDependencyRow> Dependencies { get; }

    public bool HasDependencies => Dependencies.Count > 0;

    public IReadOnlyList<PluginSettingRow> Settings { get; }

    public bool HasSettings => Settings.Count > 0;

    public Bitmap? Icon { get; }

    public bool HasIcon => Icon is not null;

    /// <summary>The first letter of the name, shown when the plugin has no icon.</summary>
    public string Monogram { get; }

    /// <summary>A color for the monogram tile, stable per plugin.</summary>
    public IBrush MonogramBrush => new SolidColorBrush(Palette[(int)((uint)StableHash(Id) % Palette.Length)]);

    /// <summary>What the switch will do once the project reloads, when that differs from now.</summary>
    public string PendingText => IsEnabled ? "Loads after the project reloads" : "Unloads after the project reloads";

    public string Subtitle => string.Join(" · ", new[] { Version.Length > 0 ? $"v{Version}" : "", Authors, Id }.Where(s => s.Length > 0));

    /// <summary>Applies a change made elsewhere without asking to switch again.</summary>
    public void SetEnabledQuietly(bool enabled)
    {
        _updating = true;
        IsEnabled = enabled;
        _updating = false;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(PendingText));
        if (!_updating)
            _ = _setEnabled(this, value);
    }

    [RelayCommand]
    private void OpenFolder() => Open(Directory);

    [RelayCommand]
    private void OpenHomepage()
    {
        if (Homepage is not null)
            Open(Homepage.ToString());
    }

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    private static PluginPermissionChip Chip(PluginPermissions permission, bool undeclared) =>
        new(PluginPermissionNames.GetName(permission), PluginPermissionNames.GetDisplayName(permission), PermissionDescriptions.GetValueOrDefault(permission, ""), undeclared);

    private static PluginDependencyRow Dependency(PluginDependency dependency, PluginLoadReport installed)
    {
        var range = dependency.Versions.IsAny ? "any version" : dependency.Versions.ToString();
        if (installed.Find(dependency.Id) is not { Manifest: { } manifest } entry)
            return new PluginDependencyRow(dependency.Id, range, dependency.Optional, "Not installed", dependency.Optional);
        var version = manifest.Version.ToString(3);
        if (!dependency.Versions.IsSatisfiedBy(manifest.Version))
            return new PluginDependencyRow(dependency.Id, range, dependency.Optional, $"{version} installed, out of range", false);
        return entry.State switch
        {
            PluginState.Disabled => new PluginDependencyRow(dependency.Id, range, dependency.Optional, $"{version} switched off", dependency.Optional),
            PluginState.Failed or PluginState.Skipped => new PluginDependencyRow(dependency.Id, range, dependency.Optional, $"{version} did not load", dependency.Optional),
            _ => new PluginDependencyRow(dependency.Id, range, dependency.Optional, $"{version} installed", true)
        };
    }

    private static Bitmap? LoadIcon(string directory, string? file)
    {
        if (string.IsNullOrEmpty(file))
            return null;
        var path = Path.Combine(directory, file);
        try
        {
            return File.Exists(path) ? new Bitmap(path) : null;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static readonly Color[] Palette =
        [Color.Parse("#6366F1"), Color.Parse("#0EA5E9"), Color.Parse("#10B981"), Color.Parse("#F59E0B"), Color.Parse("#EC4899"), Color.Parse("#8B5CF6")];

    private static int StableHash(string text)
    {
        var hash = 17;
        foreach (var c in text)
            hash = unchecked(hash * 31 + c);
        return hash;
    }

    private static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
        }
    }
}
