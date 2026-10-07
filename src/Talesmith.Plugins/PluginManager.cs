using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Talesmith.Plugins;

/// <summary>The outcome of changing which plugins load.</summary>
/// <param name="Scan">The plugins as they will be once the game sessions are rebuilt.</param>
/// <param name="RestartRequired">Whether game sessions must be rebuilt, with <see cref="PluginManager.Unload"/> in between, to apply the change.</param>
public sealed record PluginChange(PluginLoadReport Scan, bool RestartRequired);

public sealed class PluginEventArgs(PluginInfo plugin) : EventArgs
{
    public PluginInfo Plugin { get; } = plugin;
}

/// <summary>Keeps a project's plugins loaded across game sessions and lets the editor rescan, enable, disable and reload them.</summary>
/// <remarks>
/// Plugins load into collectible contexts the first time <see cref="Configure"/> runs, and every game configured afterwards (the edit
/// world, play sessions) shares the same plugin types. Changes never touch running games: they update <see cref="Scan"/> and set
/// <see cref="RestartRequired"/>. To apply them, dispose the games, call <see cref="Unload"/>, and build new games, which loads the plugins
/// again from disk.
/// </remarks>
public sealed class PluginManager : IDisposable
{
    private readonly Lock _gate = new();
    private readonly ILogger _logger;
    private readonly PluginConfigurationStore _store;
    private readonly Dictionary<string, PluginSettings> _settings = new(StringComparer.Ordinal);
    private PluginResolution _resolution;
    private PluginLoadReport _scan;
    private PluginGeneration? _generation;
    private string _loadedSignature = "";
    private bool _reloadRequested;

    /// <summary>Creates the manager and scans the installed plugins; nothing is loaded until <see cref="Configure"/>.</summary>
    /// <param name="options">Where plugins live; plugins are always loaded collectible.</param>
    /// <exception cref="InvalidDataException">The plugin configuration file is not valid.</exception>
    public PluginManager(PluginLoadOptions options, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        Options = options with { Collectible = true };
        _logger = logger;
        _store = options.ConfigurationFile is { } path ? PluginConfigurationStore.Open(path) : PluginConfigurationStore.InMemory();
        _resolution = PluginLoader.Resolve(Options, _store.Saved);
        _scan = new PluginLoadReport(_resolution.Entries);
    }

    public PluginLoadOptions Options { get; }

    /// <summary>The installed plugins as the next load will treat them; loadable plugins are <see cref="PluginState.Pending"/>.</summary>
    public PluginLoadReport Scan
    {
        get
        {
            lock (_gate)
                return _scan;
        }
    }

    /// <summary>The report of the last <see cref="Configure"/>, or <see cref="PluginLoadReport.Empty"/> while nothing is loaded.</summary>
    public PluginLoadReport Report { get; private set; } = PluginLoadReport.Empty;

    public bool IsLoaded
    {
        get
        {
            lock (_gate)
                return _generation is not null;
        }
    }

    /// <summary>Whether the loaded plugins differ from <see cref="Scan"/> (switched on or off, upgraded, rebuilt) or a reload was requested.</summary>
    public bool RestartRequired
    {
        get
        {
            lock (_gate)
                return IsRestartRequired();
        }
    }

    /// <summary>The loaded plugins' assemblies, including editor assemblies when <see cref="PluginLoadOptions.LoadEditorAssemblies"/> is set.</summary>
    /// <remarks>Do not keep these past <see cref="Unload"/>; references to them keep the plugins in memory.</remarks>
    public IReadOnlyList<PluginAssemblies> Assemblies
    {
        get
        {
            lock (_gate)
                return _generation?.Assemblies ?? [];
        }
    }

    /// <summary>The permission service shared by every game configured from the loaded plugins, or null while nothing is loaded.</summary>
    public IPluginPermissions? Permissions
    {
        get
        {
            lock (_gate)
                return _generation?.Permissions;
        }
    }

    /// <summary>The game's plugin configuration as last saved.</summary>
    public PluginConfiguration Configuration => _store.Saved;

    /// <summary>Raised for each plugin whose assemblies were loaded.</summary>
    public event EventHandler<PluginEventArgs>? PluginLoaded;

    /// <summary>Raised for each plugin asked to unload.</summary>
    public event EventHandler<PluginEventArgs>? PluginUnloaded;

    /// <summary>Raised when <see cref="Scan"/>, <see cref="Report"/> or <see cref="RestartRequired"/> may have changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Reads the plugin folders and the configuration file again.</summary>
    /// <exception cref="InvalidDataException">The plugin configuration file is not valid.</exception>
    public PluginLoadReport Rescan()
    {
        _store.Refresh();
        PluginLoadReport scan;
        lock (_gate)
        {
            _resolution = PluginLoader.Resolve(Options, _store.Saved);
            scan = _scan = new PluginLoadReport(_resolution.Entries);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return scan;
    }

    /// <summary>Switches a plugin on in the game's configuration, overriding its manifest.</summary>
    /// <exception cref="ArgumentException">No installed plugin has this id.</exception>
    /// <exception cref="IOException">The configuration file cannot be written.</exception>
    public PluginChange Enable(string id) => SetEnabled(id, enabled: true);

    /// <summary>Switches a plugin off in the game's configuration; plugins that require it are skipped.</summary>
    /// <exception cref="ArgumentException">No installed plugin has this id.</exception>
    /// <exception cref="IOException">The configuration file cannot be written.</exception>
    public PluginChange Disable(string id) => SetEnabled(id, enabled: false);

    /// <summary>Marks the loaded plugins for reloading, for example after rebuilding a plugin's private dependencies.</summary>
    public PluginChange RequestReload()
    {
        lock (_gate)
            _reloadRequested = _generation is not null;
        var scan = Rescan();
        return new PluginChange(scan, RestartRequired);
    }

    /// <summary>Loads the plugins if they are not loaded yet and lets them register into <paramref name="services"/>.</summary>
    /// <remarks>Registers the report, <see cref="IPluginPermissions"/> and each plugin's <see cref="IPluginSettings"/> keyed by plugin id.</remarks>
    public PluginLoadReport Configure(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        List<PluginInfo> loaded = [];
        PluginLoadReport report;
        lock (_gate)
        {
            if (_generation is null)
            {
                _store.Refresh();
                _resolution = PluginLoader.Resolve(Options, _store.Saved);
                _scan = new PluginLoadReport(_resolution.Entries);
                _generation = PluginGeneration.Load(_resolution, Options, _logger);
                _loadedSignature = Signature(_resolution);
                _reloadRequested = false;
                loaded.AddRange(_generation.Assemblies.Select(a => a.Plugin));
            }

            report = Report = _generation.Configure(services, GetSettingsLocked);
        }

        PluginLoader.Log(report, _logger);
        foreach (var plugin in loaded)
            PluginLoaded?.Invoke(this, new PluginEventArgs(plugin));
        Changed?.Invoke(this, EventArgs.Empty);
        return report;
    }

    /// <summary>Asks the loaded plugins to unload; dispose every game configured with them first.</summary>
    /// <returns>A handle to wait until the runtime has released the plugins' code.</returns>
    public PluginUnloadResult Unload()
    {
        PluginUnloadResult result;
        List<PluginInfo> unloaded;
        lock (_gate)
        {
            if (_generation is null)
                return PluginUnloadResult.None;

            unloaded = [.. _generation.Assemblies.Select(a => a.Plugin)];
            PluginLog.Unloading(_logger, unloaded.Count);
            result = _generation.Unload();
            _generation = null;
            _settings.Clear();
            _reloadRequested = false;
            Report = PluginLoadReport.Empty;
        }

        foreach (var plugin in unloaded)
            PluginUnloaded?.Invoke(this, new PluginEventArgs(plugin));
        Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }

    /// <summary>The settings of a plugin, shared with the plugin itself while it is loaded.</summary>
    public IPluginSettings GetSettings(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        lock (_gate)
            return GetSettingsLocked(id);
    }

    public void Dispose() => Unload();

    private PluginChange SetEnabled(string id, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (Scan.Find(id) is null)
            throw new ArgumentException($"No installed plugin has the id \"{id}\".", nameof(id));

        _store.SetEnabled(id, enabled);
        var scan = Rescan();
        return new PluginChange(scan, RestartRequired);
    }

    private IPluginSettings GetSettingsLocked(string id)
    {
        if (!_settings.TryGetValue(id, out var settings))
            _settings.Add(id, settings = new PluginSettings(id, _store));
        return settings;
    }

    private bool IsRestartRequired() => _generation is not null && (_reloadRequested || Signature(_resolution) != _loadedSignature);

    /// <summary>Identifies the plugins a resolution loads, down to the build of their assemblies.</summary>
    private string Signature(PluginResolution resolution)
    {
        var text = new StringBuilder();
        foreach (var plugin in resolution.LoadOrder)
        {
            var candidate = plugin.Candidate;
            text.Append(CultureInfo.InvariantCulture, $"{candidate.Id}|{candidate.Manifest.Version}|{candidate.Directory}|{Stamp(candidate.AssemblyPath)}");
            if (Options.LoadEditorAssemblies && candidate.EditorAssemblyPath is { } editor)
                text.Append(CultureInfo.InvariantCulture, $"|{Stamp(editor)}");
            text.Append(CultureInfo.InvariantCulture, $"|{string.Join(',', plugin.Dependencies)};");
        }

        return text.ToString();
    }

    private static long Stamp(string path) => File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0;
}
