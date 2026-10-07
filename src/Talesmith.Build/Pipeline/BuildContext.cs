using Talesmith.Assets.Database;
using Talesmith.Build.Content;
using Talesmith.Build.Packaging;
using Talesmith.Build.Player;
using Talesmith.Plugins;
using Talesmith.Runtime.Hosting;
using Talesmith.Scripting.Compiler;

namespace Talesmith.Build.Pipeline;

/// <summary>The state a build's steps share: the request, the paths, what earlier steps produced, and the log.</summary>
public sealed class BuildContext : IBuildOutput
{
    private readonly IProgress<BuildLogEntry>? _log;
    private readonly List<BuildLogEntry> _warnings = [];
    private readonly List<BuildLogEntry> _errors = [];
    private readonly Lock _lock = new();

    internal BuildContext(BuildRequest request, GameSettings game, IPlatformLayout layout, IProgress<BuildLogEntry>? log)
    {
        Request = request;
        Game = game;
        Layout = layout;
        _log = log;
        Name = BuildNames.Executable(request.Settings.ExecutableName ?? game.Title);
        OutputRoot = request.OutputFolder is { } output ? Path.GetFullPath(output) : request.Settings.ResolveOutputFolder(request.ProjectFolder);
        BuildDirectory = Path.Combine(OutputRoot, $"{Name}-{request.Target.RuntimeIdentifier}");
        StagingDirectory = Path.Combine(OutputRoot, $".{Name}-{request.Target.RuntimeIdentifier}.staging");
    }

    public BuildRequest Request { get; }

    public BuildSettings Settings => Request.Settings;

    public BuildTarget Target => Request.Target;

    public BuildProfile Profile => Request.Profile;

    /// <summary>The game's settings from <c>config/game.json</c>.</summary>
    public GameSettings Game { get; }

    public IPlatformLayout Layout { get; }

    /// <summary>The sanitized name of the executable and build folder.</summary>
    public string Name { get; }

    public string AssetRoot => Request.AssetRoot;

    public string OutputRoot { get; }

    /// <summary>Where the finished build goes, replacing the previous build of the same target.</summary>
    public string BuildDirectory { get; }

    /// <summary>Where the build is assembled before it replaces <see cref="BuildDirectory"/>.</summary>
    public string StagingDirectory { get; }

    /// <summary>The game folder inside the staging folder, with the content pack and loose configuration.</summary>
    public string StagingGameDirectory => Path.Combine(StagingDirectory, "game");

    public AssetDatabase Assets { get; internal set; } = null!;

    public ScriptCompilationResult? Scripts { get; internal set; }

    public ContentManifest? Content { get; internal set; }

    /// <summary>The plugins that ship: installed and switched on.</summary>
    public IReadOnlyList<PluginLoadEntry> Plugins { get; internal set; } = [];

    /// <summary>The application icon from the build settings as PNG bytes, or null.</summary>
    public byte[]? Icon { get; internal set; }

    /// <summary>The folder with the published player.</summary>
    public string? PlayerDirectory { get; internal set; }

    /// <summary>Sizes by category, filled as files are written.</summary>
    internal Dictionary<string, BuildSizeCategory> Sizes { get; } = new(StringComparer.Ordinal);

    internal List<BuildAssetSize> AssetSizes { get; } = [];

    internal Action<double?, string?>? ProgressHandler { get; set; }

    internal string? CurrentStep { get; set; }

    public IReadOnlyList<BuildLogEntry> Warnings
    {
        get
        {
            lock (_lock)
                return [.. _warnings];
        }
    }

    public IReadOnlyList<BuildLogEntry> Errors
    {
        get
        {
            lock (_lock)
                return [.. _errors];
        }
    }

    public bool HasErrors
    {
        get
        {
            lock (_lock)
                return _errors.Count > 0;
        }
    }

    public void Log(BuildLogLevel level, string message) => Write(new BuildLogEntry(DateTimeOffset.Now, level, message));

    public void Write(BuildLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entry = entry with { Step = entry.Step ?? CurrentStep };
        lock (_lock)
        {
            if (entry.Level == BuildLogLevel.Warning)
                _warnings.Add(entry);
            else if (entry.Level == BuildLogLevel.Error)
                _errors.Add(entry);
        }

        _log?.Report(entry);
    }

    public void Progress(double? fraction, string? detail = null) => ProgressHandler?.Invoke(fraction, detail);

    /// <summary>Counts a shipped file in a size category.</summary>
    internal void AddSize(string category, long size, long originalSize)
    {
        lock (_lock)
        {
            var current = Sizes.GetValueOrDefault(category) ?? new BuildSizeCategory(category, 0, 0, 0);
            Sizes[category] = current with { Files = current.Files + 1, Size = current.Size + size, OriginalSize = current.OriginalSize + originalSize };
        }
    }
}
