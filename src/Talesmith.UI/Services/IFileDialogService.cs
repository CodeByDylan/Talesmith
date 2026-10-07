namespace Talesmith.UI.Services;

/// <summary>A named set of file extensions such as ("PNG image", [".png"]).</summary>
public sealed record FileFilter(string Name, IReadOnlyList<string> Extensions);

/// <summary>Shows native file and folder pickers. Pickers open in the start folder when it exists.</summary>
public interface IFileDialogService
{
    /// <summary>Asks for an existing file; returns its local path or null when cancelled.</summary>
    Task<string?> PickFileToOpenAsync(string title, IReadOnlyList<FileFilter> filters, string? startFolder = null);

    /// <summary>Asks for one or more existing files; returns their local paths, empty when cancelled.</summary>
    Task<IReadOnlyList<string>> PickFilesToOpenAsync(string title, IReadOnlyList<FileFilter> filters, string? startFolder = null);

    /// <summary>Asks for a destination file; returns its local path or null when cancelled.</summary>
    Task<string?> PickFileToSaveAsync(string title, string suggestedName, IReadOnlyList<FileFilter> filters);

    /// <summary>Asks for a folder; returns its local path or null when cancelled.</summary>
    Task<string?> PickFolderAsync(string title, string? startFolder = null);
}
