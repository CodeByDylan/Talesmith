using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Talesmith.Assets;
using Talesmith.Editor.Particles.Preview;
using Talesmith.Editor.Projects;
using Talesmith.VFX;
using Talesmith.VFX.Presets;

namespace Talesmith.Editor.Particles.Presets;

/// <summary>What the gallery's tiles ask of the particle editor.</summary>
public interface IPresetTileHost
{
    /// <summary>Copies the preset's settings into the edited effect, or adds an emitter playing it when nothing is edited.</summary>
    void Apply(ParticlePresetTile tile);

    /// <summary>Makes the selected emitter play the preset asset.</summary>
    void Link(ParticlePresetTile tile);

    /// <summary>Opens the preset asset for editing.</summary>
    void Edit(ParticlePresetTile tile);
}

/// <summary>A preset in the gallery with a small animated preview.</summary>
public sealed partial class ParticlePresetTile : ObservableObject, IDisposable
{
    private readonly IPresetTileHost _host;
    private ParticlePreviewPlayer? _player;

    public ParticlePresetTile(string name, string description, Geometry icon, Func<ParticleSettings> create, IPresetTileHost host, AssetGuid asset = default,
        string? assetPath = null)
    {
        Name = name;
        Description = description;
        Icon = icon;
        Create = create;
        Asset = asset;
        AssetPath = assetPath;
        _host = host;
    }

    public string Name { get; }

    public string Description { get; }

    public Geometry Icon { get; }

    /// <summary>Creates a fresh copy of the preset's settings.</summary>
    public Func<ParticleSettings> Create { get; }

    /// <summary>The preset's asset, or empty for a built-in preset.</summary>
    public AssetGuid Asset { get; }

    public string? AssetPath { get; }

    public bool IsAsset => !Asset.IsEmpty;

    public string Badge => IsAsset ? "Project" : "Built-in";

    public string ToolTipText => IsAsset ? $"{Name}\n{AssetPath}" : $"{Name}\n{Description}";

    /// <summary>The thumbnail's simulation, started on first use and warmed up so it shows the effect in full.</summary>
    public ParticlePreviewPlayer Player
    {
        get
        {
            if (_player is not null)
                return _player;
            var settings = Create();
            if (settings.Seed == 0)
                settings.Seed = 7;
            _player = new ParticlePreviewPlayer(settings);
            for (var i = 0; i < 45; i++)
                _player.Advance(1 / 30.0);
            return _player;
        }
    }

    [RelayCommand]
    private void Apply() => _host.Apply(this);

    [RelayCommand]
    private void Link() => _host.Link(this);

    [RelayCommand]
    private void Edit() => _host.Edit(this);

    public void Dispose() => _player?.Dispose();
}

/// <summary>The built-in presets and the project's <c>.tparticles</c> files.</summary>
public sealed partial class ParticlePresetGallery : ObservableObject, IDisposable
{
    private readonly IProjectService _project;
    private readonly ParticleSettingsCodec _codec;
    private readonly ILogger _logger;
    private int _version;

    public ParticlePresetGallery(IProjectService project, ParticleSettingsCodec codec, ILogger<ParticlePresetGallery> logger)
    {
        _project = project;
        _codec = codec;
        _logger = logger;
    }

    public ObservableCollection<ParticlePresetTile> Tiles { get; } = [];

    /// <summary>Lists the presets again, reading project presets in the background.</summary>
    public async Task RefreshAsync(IPresetTileHost host)
    {
        var version = ++_version;
        var builtIn = BuiltInParticlePresets.All.Select(p => new ParticlePresetTile(p.Name, p.Description, ParticleIcons.ForPreset(p.Name), p.Create, host)).ToList();
        var assets = _project.Catalog.Entries.Where(e => e.Path.EndsWith(ParticlePresetSerializer.Extension, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var modules = _codec.IsAvailable ? _codec.Modules : ParticleModuleRegistry.Empty;
        var root = _project.Project.AssetRoot;
        var loaded = await Task.Run(() => assets.Select(entry => Load(root, entry, modules)).Where(p => p is not null).ToList()).ConfigureAwait(true);
        if (version != _version)
            return;
        Clear();
        foreach (var (entry, preset) in loaded.Select(p => p!.Value))
        {
            var settings = preset.Settings;
            Tiles.Add(new ParticlePresetTile(preset.Name, entry.Path, UI.Icons.Sparkles, () => settings.Clone(modules), host, entry.Guid, entry.Path));
        }

        foreach (var tile in builtIn)
            Tiles.Add(tile);
    }

    public void Dispose() => Clear();

    private (AssetCatalogEntry Entry, ParticlePreset Preset)? Load(string root, AssetCatalogEntry entry, ParticleModuleRegistry modules)
    {
        try
        {
            using var stream = File.OpenRead(Path.Combine(root, entry.Path));
            return (entry, ParticlePresetSerializer.Deserialize(stream, modules));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            ParticleEditorLog.PresetUnreadable(_logger, ex, entry.Path);
            return null;
        }
    }

    private void Clear()
    {
        foreach (var tile in Tiles)
            tile.Dispose();
        Tiles.Clear();
    }
}
