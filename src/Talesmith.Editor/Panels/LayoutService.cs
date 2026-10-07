using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using Talesmith.Assets;
using Talesmith.Editor.Projects;
using Talesmith.UI.Docking;

namespace Talesmith.Editor.Panels;

/// <summary>The dock layout of the editor window: the built-in and saved presets, and the current layout, kept per project in
/// <c>.talesmith/layout.json</c>.</summary>
public sealed class LayoutService : IAsyncDisposable
{
    public const string FileName = "layout.json";
    public const string DefaultPreset = "Default";
    public const string TileMappingPreset = "Tile Mapping";
    public const string EffectsPreset = "Effects";
    public const string MinimalPreset = "Minimal";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly PanelRegistry _panels;
    private readonly string _path;
    private readonly DispatcherTimer _saveTimer;
    private readonly DockLayoutPresets _saved;
    private bool _dirty;

    public LayoutService(PanelRegistry panels, EditorProject project)
    {
        _panels = panels;
        _path = project.GetStatePath(FileName);
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _saveTimer.Tick += (_, _) => _ = SaveAsync();

        var (layout, preset, saved) = Load();
        _saved = saved;
        CurrentPreset = preset;
        Layout = layout ?? Build(DefaultPreset);
        Layout.Fallback = BuildFallback();
        Layout.Changed += OnLayoutChanged;
    }

    public DockLayout Layout { get; private set; }

    /// <summary>The preset the layout was last set from; the layout may have changed since.</summary>
    public string CurrentPreset { get; private set; }

    /// <summary>The built-in presets followed by the user's saved ones.</summary>
    public IReadOnlyList<string> Presets => [.. BuiltInPresets, .. _saved.Names.Where(n => !BuiltInPresets.Contains(n, StringComparer.OrdinalIgnoreCase))];

    public static IReadOnlyList<string> BuiltInPresets { get; } = [DefaultPreset, TileMappingPreset, EffectsPreset, MinimalPreset];

    /// <summary>Raised after <see cref="Layout"/> was replaced, such as by a preset, or a preset was saved.</summary>
    public event EventHandler? LayoutReplaced;

    /// <summary>Switches to a preset.</summary>
    public void ApplyPreset(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        DockLayout? layout = null;
        if (!BuiltInPresets.Contains(name, StringComparer.OrdinalIgnoreCase) && _saved.TryLoad(name, _panels.Contains, out var saved))
            layout = saved;
        layout ??= Build(name);
        CurrentPreset = name;
        Replace(layout);
    }

    /// <summary>Saves the current layout as a preset with a name.</summary>
    public void SavePreset(string name)
    {
        _saved.Save(name, Layout);
        CurrentPreset = name;
        MarkDirty();
        LayoutReplaced?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shows a panel where it was last, or where the default layout puts it, and activates it.</summary>
    public void ShowPanel(string panelId)
    {
        if (_panels.Contains(panelId))
            Layout.EnsureVisible(panelId);
    }

    /// <summary>Builds a built-in preset from the registered panels; unknown names give the default layout.</summary>
    public DockLayout Build(string preset) => Build(preset, everyPanel: false);

    /// <summary>The default layout with every panel, which places panels that open later where they belong.</summary>
    private DockLayout BuildFallback() => Build(DefaultPreset, everyPanel: true);

    private DockLayout Build(string preset, bool everyPanel)
    {
        var all = _panels.Panels;
        string[] At(DockLocation location, Func<EditorPanelInfo, bool>? include = null) =>
            [.. all.Where(p => p.Location == location && (include?.Invoke(p) ?? (everyPanel || p.OpenByDefault))).Select(p => p.Id)];
        bool Known(string id) => _panels.Contains(id);
        string[] Only(params string[] ids) => [.. ids.Where(Known)];
        string[] Without(string[] ids, params string[] excluded) => [.. ids.Where(id => !excluded.Contains(id))];

        var left = At(DockLocation.Left);
        var center = At(DockLocation.Center);
        var right = At(DockLocation.Right);
        var bottom = At(DockLocation.Bottom);

        DockNode root = preset.ToLowerInvariant() switch
        {
            "tile mapping" => Row(
                Group("left", left, 0.17),
                Column("middle", 0.57, Group("center", center, 0.74), Group("bottom", Without(bottom, PanelIds.TileMap), 0.26)),
                Column("right", 0.26, Group("tools", Only(PanelIds.TileMap), 0.7), Group("properties", Without(right, PanelIds.TileMap), 0.3))),
            "effects" => Row(
                Group("left", left, 0.16),
                Column("middle", 0.56, Group("center", center, 0.7), Group("bottom", Without(bottom, PanelIds.Particles, PanelIds.Lighting), 0.3)),
                Column("right", 0.28, Group("effects", Only(PanelIds.Particles, PanelIds.Lighting), 0.55), Group("properties", Without(right, PanelIds.Particles, PanelIds.Lighting), 0.45))),
            "minimal" => Row(
                Group("center", Only(PanelIds.Scene, PanelIds.Game), 0.76),
                Group("right", Only(PanelIds.Inspector), 0.24)),
            _ => Row(
                Group("left", left, 0.18),
                Column("middle", 0.58, Group("center", center, 0.68), Group("bottom", bottom, 0.32)),
                Group("right", right, 0.24))
        };
        var layout = new DockLayout(root);
        if (Known(PanelIds.Scene))
            layout.ActivatePanel(PanelIds.Scene);
        return layout;
    }

    public async Task SaveAsync()
    {
        _saveTimer.Stop();
        if (!_dirty)
            return;
        _dirty = false;
        var json = new JsonObject
        {
            ["version"] = 1,
            ["preset"] = CurrentPreset,
            ["layout"] = JsonNode.Parse(DockLayoutSerializer.Serialize(Layout)),
            ["presets"] = JsonNode.Parse(_saved.ToJson())
        };
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await AtomicFile.WriteAllTextAsync(_path, json.ToJsonString(WriteOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The layout is a convenience; the next session starts from the default layout.
        }
    }

    public async ValueTask DisposeAsync() => await SaveAsync();

    private static DockSplit Row(params DockNode[] children) => new("root", DockOrientation.Horizontal, children.Where(HasPanels));

    private static DockSplit Column(string id, double size, params DockNode[] children) =>
        new(id, DockOrientation.Vertical, children.Where(HasPanels)) { Size = size };

    private static DockGroup Group(string id, string[] panels, double size) => new(id, panels) { Size = size };

    private static bool HasPanels(DockNode node) => node switch
    {
        DockGroup group => group.Panels.Count > 0,
        DockSplit split => split.Children.Any(HasPanels),
        _ => false
    };

    private (DockLayout? Layout, string Preset, DockLayoutPresets Saved) Load()
    {
        try
        {
            if (File.Exists(_path) && JsonNode.Parse(File.ReadAllText(_path)) is JsonObject json)
            {
                var saved = DockLayoutPresets.FromJson(json["presets"]?.ToJsonString());
                var preset = json["preset"]?.GetValue<string>() ?? DefaultPreset;
                DockLayoutSerializer.TryDeserialize(json["layout"]?.ToJsonString(), _panels.Contains, out var layout);
                return (layout is { } l && l.Panels.Contains(PanelIds.Scene) ? l : null, preset, saved);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
        {
        }

        return (null, DefaultPreset, new DockLayoutPresets());
    }

    private void Replace(DockLayout layout)
    {
        Layout.Changed -= OnLayoutChanged;
        layout.Fallback = BuildFallback();
        Layout = layout;
        Layout.Changed += OnLayoutChanged;
        MarkDirty();
        LayoutReplaced?.Invoke(this, EventArgs.Empty);
    }

    private void OnLayoutChanged(object? sender, DockLayoutChangedEventArgs e)
    {
        if (e.Kind != DockChangeKind.Activation)
            MarkDirty();
    }

    private void MarkDirty()
    {
        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }
}
