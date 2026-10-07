using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Talesmith.UI.Services;

namespace Talesmith.UI.Controls;

/// <summary>What a <see cref="PathField"/> browses for.</summary>
public enum PathFieldMode
{
    OpenFile,
    SaveFile,
    Folder
}

/// <summary>An editable file or folder path with a browse button that opens the system picker.</summary>
[TemplatePart(BrowsePart, typeof(Button))]
public class PathField : TemplatedControl
{
    private const string BrowsePart = "PART_Browse";

    public static readonly StyledProperty<string?> PathProperty =
        AvaloniaProperty.Register<PathField, string?>(nameof(Path), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<PathFieldMode> ModeProperty =
        AvaloniaProperty.Register<PathField, PathFieldMode>(nameof(Mode));

    public static readonly StyledProperty<string> DialogTitleProperty =
        AvaloniaProperty.Register<PathField, string>(nameof(DialogTitle), "Choose a file");

    public static readonly StyledProperty<IReadOnlyList<FileFilter>?> FiltersProperty =
        AvaloniaProperty.Register<PathField, IReadOnlyList<FileFilter>?>(nameof(Filters));

    public static readonly StyledProperty<string?> PlaceholderTextProperty =
        AvaloniaProperty.Register<PathField, string?>(nameof(PlaceholderText));

    private Button? _browse;

    public string? Path
    {
        get => GetValue(PathProperty);
        set => SetValue(PathProperty, value);
    }

    public PathFieldMode Mode
    {
        get => GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public string DialogTitle
    {
        get => GetValue(DialogTitleProperty);
        set => SetValue(DialogTitleProperty, value);
    }

    /// <summary>File types offered by the picker; all files when null.</summary>
    public IReadOnlyList<FileFilter>? Filters
    {
        get => GetValue(FiltersProperty);
        set => SetValue(FiltersProperty, value);
    }

    public string? PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _browse?.Click -= OnBrowseClick;
        _browse = e.NameScope.Find<Button>(BrowsePart);
        _browse?.Click += OnBrowseClick;
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;

        var start = await StartFolderAsync(storage);
        var types = Filters?.Select(f => new FilePickerFileType(f.Name) { Patterns = f.Extensions.Select(x => "*" + x).ToList() }).ToList();

        string? chosen = Mode switch
        {
            PathFieldMode.Folder => await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = DialogTitle, SuggestedStartLocation = start }) is [var folder, ..]
                ? folder.TryGetLocalPath()
                : null,
            PathFieldMode.SaveFile => (await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = DialogTitle,
                SuggestedStartLocation = start,
                SuggestedFileName = System.IO.Path.GetFileName(Path),
                FileTypeChoices = types
            }))?.TryGetLocalPath(),
            _ => await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = DialogTitle, SuggestedStartLocation = start, FileTypeFilter = types }) is [var file, ..]
                ? file.TryGetLocalPath()
                : null
        };

        if (chosen is not null)
            Path = chosen;
    }

    private async Task<IStorageFolder?> StartFolderAsync(IStorageProvider storage)
    {
        var current = Path;
        if (string.IsNullOrWhiteSpace(current))
            return null;
        var folder = Directory.Exists(current) ? current : System.IO.Path.GetDirectoryName(current);
        return folder is not null && Directory.Exists(folder) ? await storage.TryGetFolderFromPathAsync(folder) : null;
    }
}
