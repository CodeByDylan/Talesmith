using Microsoft.Extensions.Logging;
using Talesmith.Editor.Projects;

namespace Talesmith.Editor.Lighting;

/// <summary>Reads and writes the project's <c>.tlighting</c> presets.</summary>
public static class LightingPresetStore
{
    /// <summary>The folder new presets are saved in, relative to the asset root.</summary>
    public const string Folder = "lighting";

    /// <summary>Reads every lighting preset the project's asset catalog lists, on the thread pool.</summary>
    public static async Task<IReadOnlyList<(string Path, LightingPreset Preset)>> LoadAsync(IProjectService project, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(project);
        var root = project.Project.AssetRoot;
        var paths = project.Catalog.Entries.Select(e => e.Path).Where(p => p.EndsWith(LightingPreset.Extension, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        if (paths.Count == 0)
            return [];
        return await Task.Run(() =>
        {
            var presets = new List<(string, LightingPreset)>();
            foreach (var path in paths)
            {
                try
                {
                    presets.Add((path, LightingPreset.Deserialize(File.ReadAllText(Path.Combine(root, path)))));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
                {
                    LightingLog.PresetUnreadable(logger, ex, path);
                }
            }

            return presets;
        }).ConfigureAwait(true);
    }

    /// <summary>Saves a preset in <see cref="Folder"/> under a name that is not taken and adds it to the asset database; returns its asset path.</summary>
    public static async Task<string> SaveAsync(IProjectService project, LightingPreset preset)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(preset);
        var directory = Path.Combine(project.Project.AssetRoot, Folder);
        var safe = string.Concat(preset.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)).Trim();
        if (safe.Length == 0)
            safe = "Lighting";
        var path = Path.Combine(directory, safe + LightingPreset.Extension);
        for (var i = 2; File.Exists(path); i++)
            path = Path.Combine(directory, $"{safe} {i}{LightingPreset.Extension}");
        var text = preset.Serialize();
        await Task.Run(() =>
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, text);
        }).ConfigureAwait(true);
        var assetPath = project.Project.ToAssetPath(path) ?? Path.GetFileName(path);
        if (project.Database is { } database)
            await database.RefreshAsync([assetPath]).ConfigureAwait(true);
        return assetPath;
    }
}

internal static partial class LightingLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "The lighting preset {Path} could not be read")]
    public static partial void PresetUnreadable(ILogger logger, Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "The lighting preset {Name} could not be saved")]
    public static partial void PresetSaveFailed(ILogger logger, Exception exception, string name);
}
