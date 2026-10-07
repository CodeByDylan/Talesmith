using Talesmith.Assets;
using Talesmith.Assets.Database;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Editor.Projects;

/// <summary>A game project on disk: a folder with <c>assets/config/game.json</c>, and editor state in <c>.talesmith/</c>.</summary>
public sealed class EditorProject
{
    public const string AssetFolderName = "assets";

    public EditorProject(string folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        Folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        AssetRoot = Path.Combine(Folder, AssetFolderName);
        StateFolder = Path.Combine(Folder, AssetDatabaseOptions.StateFolderName);
        Name = ReadTitle() ?? Path.GetFileName(Folder);
    }

    public string Folder { get; }

    /// <summary>The folder containing the game's assets.</summary>
    public string AssetRoot { get; }

    /// <summary>Editor state that is not part of the game, such as the layout and caches.</summary>
    public string StateFolder { get; }

    /// <summary>The game's title, or the folder's name when it has none.</summary>
    public string Name { get; }

    public string SettingsFile => Path.Combine(AssetRoot, GameSettings.FileName);

    /// <summary>Whether <paramref name="folder"/> is a project folder.</summary>
    public static bool IsProject(string folder) => File.Exists(Path.Combine(folder, AssetFolderName, GameSettings.FileName));

    /// <summary>Finds the project folder for a project folder, its asset folder or any file inside it, such as <c>game.json</c>.</summary>
    public static bool TryResolve(string path, out string folder)
    {
        folder = "";
        if (string.IsNullOrWhiteSpace(path))
            return false;
        var current = Path.GetFullPath(path);
        if (File.Exists(current))
            current = Path.GetDirectoryName(current)!;
        for (var directory = new DirectoryInfo(current); directory is not null; directory = directory.Parent)
        {
            if (IsProject(directory.FullName))
            {
                folder = directory.FullName;
                return true;
            }
        }

        return false;
    }

    /// <summary>The path of a file in the editor state folder.</summary>
    public string GetStatePath(string relativePath) => Path.Combine(StateFolder, relativePath);

    /// <summary>The absolute path of an asset path, such as <c>scenes/main.tscene</c>.</summary>
    public string ToAbsolutePath(string assetPath) => Path.GetFullPath(Path.Combine(AssetRoot, AssetPath.Normalize(assetPath)));

    /// <summary>The asset path of a file inside the asset folder, or null for files outside it.</summary>
    public string? ToAssetPath(string absolutePath)
    {
        var relative = Path.GetRelativePath(AssetRoot, Path.GetFullPath(absolutePath));
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? null : AssetPath.Normalize(relative);
    }

    private string? ReadTitle()
    {
        try
        {
            var title = GameSettings.Load(AssetRoot).Title;
            return string.IsNullOrWhiteSpace(title) || title == new GameSettings().Title ? null : title;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
