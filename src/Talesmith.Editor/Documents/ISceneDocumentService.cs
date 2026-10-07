using Talesmith.Assets;

namespace Talesmith.Editor.Documents;

/// <summary>Opens, creates and saves the scene being edited. One scene is open at a time.</summary>
public interface ISceneDocumentService
{
    /// <summary>The open scene, or null while the project is still opening.</summary>
    SceneDocumentModel? Active { get; }

    /// <summary>Asset paths of recently opened scenes, the most recent first.</summary>
    IReadOnlyList<string> RecentScenes { get; }

    /// <summary>Whether a scene is being read from disk.</summary>
    bool IsLoading { get; }

    /// <summary>Raised on the UI thread after another scene became <see cref="Active"/>.</summary>
    event EventHandler? ActiveChanged;

    /// <summary>Raised when the open scene's title, path or unsaved state changed, or <see cref="RecentScenes"/> changed.</summary>
    event EventHandler? StateChanged;

    /// <summary>Raised on the UI thread after the open scene was written to its file, by saving or autosave, so assets it shows can be saved with it.</summary>
    event EventHandler? Saved;

    /// <summary>Opens a new, unsaved scene with a camera, asking to save the open one first; returns false when cancelled.</summary>
    Task<bool> NewAsync();

    /// <summary>Opens a scene by asset path, asking to save the open one first; returns false when cancelled or the scene could not be read.</summary>
    Task<bool> OpenAsync(string assetPath);

    Task<bool> OpenAsync(AssetGuid scene);

    /// <summary>Asks for a scene file and opens it.</summary>
    Task<bool> OpenWithDialogAsync();

    /// <summary>Saves the open scene, asking for a file name when it was never saved; returns false when cancelled or writing failed.</summary>
    Task<bool> SaveAsync();

    /// <summary>Saves the open scene under a new name; asks for one when <paramref name="assetPath"/> is null.</summary>
    Task<bool> SaveAsAsync(string? assetPath = null);

    /// <summary>Discards unsaved changes by reading the scene's file again.</summary>
    Task RevertAsync();

    /// <summary>Opens the scene the project was last edited with, the game's start scene, or a new scene.</summary>
    Task OpenStartupSceneAsync();

    /// <summary>Asks to save unsaved changes; returns false when the user cancelled.</summary>
    Task<bool> ConfirmDiscardAsync();
}
