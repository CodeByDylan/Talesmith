using Avalonia.Platform.Storage;

namespace Talesmith.UI.Services;

/// <summary>Shows file pickers through the window's storage provider.</summary>
public sealed class FileDialogService(WindowHost host) : IFileDialogService
{
    public async Task<string?> PickFileToOpenAsync(string title, IReadOnlyList<FileFilter> filters, string? startFolder = null)
    {
        var files = await OpenAsync(title, filters, startFolder, allowMultiple: false);
        return files.Count == 0 ? null : files[0];
    }

    public Task<IReadOnlyList<string>> PickFilesToOpenAsync(string title, IReadOnlyList<FileFilter> filters, string? startFolder = null) =>
        OpenAsync(title, filters, startFolder, allowMultiple: true);

    public async Task<string?> PickFileToSaveAsync(string title, string suggestedName, IReadOnlyList<FileFilter> filters)
    {
        if (host.TopLevel?.StorageProvider is not { CanSave: true } storage)
            return null;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = filters.Count > 0 && filters[0].Extensions.Count > 0 ? filters[0].Extensions[0].TrimStart('.') : null,
            FileTypeChoices = ToFileTypes(filters),
            ShowOverwritePrompt = true
        });

        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickFolderAsync(string title, string? startFolder = null)
    {
        if (host.TopLevel?.StorageProvider is not { CanPickFolder: true } storage)
            return null;

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false, SuggestedStartLocation = await StartLocationAsync(storage, startFolder) });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    private async Task<IReadOnlyList<string>> OpenAsync(string title, IReadOnlyList<FileFilter> filters, string? startFolder, bool allowMultiple)
    {
        if (host.TopLevel?.StorageProvider is not { CanOpen: true } storage)
            return [];

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = allowMultiple,
            SuggestedStartLocation = await StartLocationAsync(storage, startFolder),
            FileTypeFilter = ToFileTypes(filters)
        });

        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    private static async Task<IStorageFolder?> StartLocationAsync(IStorageProvider storage, string? folder) =>
        string.IsNullOrEmpty(folder) || !Directory.Exists(folder) ? null : await storage.TryGetFolderFromPathAsync(folder);

    private static List<FilePickerFileType> ToFileTypes(IReadOnlyList<FileFilter> filters) =>
        filters.Select(f => new FilePickerFileType(f.Name) { Patterns = f.Extensions.Select(e => "*" + e).ToList() }).ToList();
}
