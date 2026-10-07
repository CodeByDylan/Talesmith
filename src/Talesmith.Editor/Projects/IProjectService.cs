using System.Text.Json.Nodes;
using Talesmith.Assets;
using Talesmith.Assets.Database;
using Talesmith.Avalonia.Hosting;
using Talesmith.Plugins;
using Talesmith.Runtime.Hosting;
using Talesmith.Scripting;

namespace Talesmith.Editor.Projects;

/// <summary>The open project: its settings, asset database, plugins and the games the editor runs for it.</summary>
/// <remarks>
/// The editor's services live as long as one project is open; opening another project or closing it rebuilds them, so services can take
/// the project for granted. The asset database is scanned in the background after the project opens: check <see cref="IsReady"/> or await
/// <see cref="WhenReady"/> before relying on it.
/// </remarks>
public interface IProjectService
{
    EditorProject Project { get; }

    /// <summary>The game's settings from <c>config/game.json</c>.</summary>
    GameSettings Settings { get; }

    /// <summary>The asset catalog shared by the editor and every game it starts, kept up to date by the database.</summary>
    AssetCatalog Catalog { get; }

    /// <summary>The project's asset database, or null until the edit game exists.</summary>
    AssetDatabase? Database { get; }

    /// <summary>Reloads the edit game's assets when their files change; null until the project is ready.</summary>
    AssetHotReload? HotReload { get; }

    /// <summary>The project's plugins, loaded once and shared by the edit game and play sessions.</summary>
    PluginManager Plugins { get; }

    /// <summary>The game whose world mirrors the open scene, in <c>ExecutionModes.Edit</c>; null until the project finished opening, and
    /// replaced when scripts need a new one (see <see cref="EditSessionChanged"/>).</summary>
    GameSession? EditSession { get; }

    /// <summary>Whether the edit game exists and the first asset scan finished.</summary>
    bool IsReady { get; }

    /// <summary>The progress of the running asset scan, or null when none runs.</summary>
    AssetScanProgress? ScanProgress { get; }

    /// <summary>Completes when the project is ready; faults when it could not open.</summary>
    Task WhenReady { get; }

    /// <summary>Raised on the UI thread when <see cref="IsReady"/>, <see cref="ScanProgress"/> or <see cref="Settings"/> changed.</summary>
    event EventHandler? StatusChanged;

    /// <summary>Raised on the UI thread after <see cref="EditSession"/> was replaced by <see cref="ReplaceEditSessionAsync"/>.</summary>
    event EventHandler<EditSessionChangedEventArgs>? EditSessionChanged;

    /// <summary>Starts a game for the project with its plugins and asset catalog, such as a play session.</summary>
    Task<GameSession> CreateSessionAsync(GameSessionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Replaces the edit game with a new one that runs <paramref name="scripts"/>, for scripts that cannot be swapped into the running one.
    /// The open scene, selection and camera stay as they are. Call it on the UI thread once <see cref="EditSession"/> exists.</summary>
    Task ReplaceEditSessionAsync(ScriptAssembly? scripts, CancellationToken cancellationToken = default);

    /// <summary>Changes <c>config/game.json</c> and reloads <see cref="Settings"/>.</summary>
    /// <exception cref="InvalidDataException">The changed settings are not valid.</exception>
    Task UpdateSettingsAsync(Action<JsonObject> change, CancellationToken cancellationToken = default);

    /// <summary>Scans the asset folder again.</summary>
    Task RescanAsync();

    /// <summary>Closes this project and opens another, asking to save changes first; returns false when the user cancelled.</summary>
    Task<bool> OpenAsync(string projectFolder);

    /// <summary>Closes the project and returns to the hub, asking to save changes first; returns false when the user cancelled.</summary>
    Task<bool> CloseAsync();

    /// <summary>Opens the project again with fresh plugins, edit game and editor services, asking to save changes first; returns false when the
    /// user cancelled.</summary>
    Task<bool> ReloadAsync();
}
