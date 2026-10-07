using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Assets;
using Talesmith.Assets.Maps;
using Talesmith.Assets.Textures;
using Talesmith.Editor.Projects;
using Talesmith.Editor.TileMaps.Rendering;
using Talesmith.UI.Services;

namespace Talesmith.Editor.TileMaps.Dialogs;

/// <summary>Slices an image from the project or from disk into a new tileset, with a live preview of the grid, like Hexy's import dialog.</summary>
/// <remarks>Hexy Forge copies its sheet's import settings as text ("Tile size: 128 × 148", "Margin", "Spacing", "Columns"); pasting them fills in
/// the slicing.</remarks>
public sealed partial class TilesetImportDialogViewModel : TileMapDialogViewModel
{
    private static readonly FileFilter ImageFilter = new("Images", [".png", ".jpg", ".jpeg", ".webp", ".bmp"]);
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp"];

    private readonly IProjectService _project;
    private readonly IFileDialogService _files;
    private readonly TileMap _map;
    private TextureAsset? _texture;
    private string? _absolutePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage), nameof(Summary), nameof(ValidationMessage), nameof(ImageSizeText))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private Bitmap? _image;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImageName))]
    private string? _imagePath;

    [ObservableProperty]
    private string? _selectedProjectImage;

    public string? ImageName => ImagePath is null ? null : Path.GetFileName(ImagePath);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private string _name = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(ValidationMessage), nameof(ForgeMessage))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private int _tileWidth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(ValidationMessage), nameof(ForgeMessage))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private int _tileHeight;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(ValidationMessage), nameof(ForgeMessage))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private int _margin;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(ValidationMessage), nameof(ForgeMessage))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private int _spacing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private string? _loadError;

    /// <summary>The columns Hexy Forge reported for its sheet, to check the slicing against.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ForgeMessage), nameof(HasForgeSettings))]
    private int? _forgeColumns;

    [ObservableProperty]
    private bool _isLoading;

    public TilesetImportDialogViewModel(IProjectService project, IFileDialogService files, TileMap map)
    {
        _project = project;
        _files = files;
        _map = map;
        _tileWidth = (int)Math.Round(map.Layout.CellSize.X);
        _tileHeight = (int)Math.Round(map.Layout.CellSize.Y);
        ProjectImages = project.Database?.Assets.Where(r => !r.IsFolder && ImageExtensions.Contains(AssetPath.GetExtension(r.Path))).Select(r => r.Path)
            .Order(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
    }

    /// <summary>The images in the project's assets.</summary>
    public IReadOnlyList<string> ProjectImages { get; }

    public bool HasProjectImages => ProjectImages.Count > 0;

    public bool HasImage => Image is not null;

    public string ImageSizeText => Image is { } image ? $"{image.PixelSize.Width} × {image.PixelSize.Height} px" : "";

    public string CellSizeText => string.Create(CultureInfo.CurrentCulture, $"{_map.Layout.CellSize.X:0.#} × {_map.Layout.CellSize.Y:0.#} px cells");

    public bool HasForgeSettings => ForgeColumns is not null;

    public string Summary
    {
        get
        {
            var (columns, rows) = Grid();
            return Image is null ? "No image selected" : $"{columns} columns × {rows} rows = {columns * rows:N0} tiles";
        }
    }

    /// <summary>Whether the slicing matches the columns Hexy Forge reported, or null without Forge settings.</summary>
    public string? ForgeMessage => ForgeColumns is not { } expected ? null
        : Image is null ? $"Hexy Forge sheet with {expected} columns"
        : Grid().Columns == expected ? $"Matches the Hexy Forge sheet: {expected} columns"
        : $"Hexy Forge reported {expected} columns, but this slicing gives {Grid().Columns}. Is this the sheet it exported?";

    public string? ValidationMessage
    {
        get
        {
            if (LoadError is not null)
                return LoadError;
            if (Image is null)
                return null;
            if (string.IsNullOrWhiteSpace(Name))
                return "Enter a name for the tileset.";
            if (TileWidth <= 0 || TileHeight <= 0)
                return "Tile width and height must be greater than zero.";
            var (columns, rows) = Grid();
            if (columns * rows == 0)
                return "The tile size, margin and spacing leave no complete tiles in this image.";
            return columns * rows > TileCell.MaxTileId + 1 ? $"The tileset would hold more than {TileCell.MaxTileId + 1:N0} tiles." : null;
        }
    }

    /// <summary>Applies import settings copied from Hexy Forge; returns false when the text holds none.</summary>
    public bool ApplyForgeSettings(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var size = TileSizePattern().Match(text);
        var margin = SettingPattern("Margin").Match(text);
        var spacing = SettingPattern("Spacing").Match(text);
        var columns = SettingPattern("Columns").Match(text);
        if (!size.Success && !margin.Success && !spacing.Success)
            return false;
        if (size.Success)
        {
            TileWidth = int.Parse(size.Groups[1].Value, CultureInfo.InvariantCulture);
            TileHeight = int.Parse(size.Groups[2].Value, CultureInfo.InvariantCulture);
        }

        if (margin.Success)
            Margin = int.Parse(margin.Groups[1].Value, CultureInfo.InvariantCulture);
        if (spacing.Success)
            Spacing = int.Parse(spacing.Groups[1].Value, CultureInfo.InvariantCulture);
        ForgeColumns = columns.Success ? int.Parse(columns.Groups[1].Value, CultureInfo.InvariantCulture) : null;
        return true;
    }

    /// <summary>Loads an image from disk.</summary>
    public async Task LoadFileAsync(string path)
    {
        try
        {
            IsLoading = true;
            var data = await Task.Run(() =>
            {
                using var stream = File.OpenRead(path);
                return TextureDecoder.Decode(stream);
            });
            SetImage(new TextureAsset(Path.GetFullPath(path), data), Path.GetFullPath(path));
            _absolutePath = Path.GetFullPath(path);
            ImagePath = _project.Project.ToAssetPath(_absolutePath) ?? _absolutePath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or AssetException)
        {
            LoadError = $"\"{Path.GetFileName(path)}\" could not be read as an image.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Loads an image of the project through the edit game, sharing the texture its sprites use.</summary>
    public async Task LoadProjectImageAsync(string assetPath)
    {
        if (_project.EditSession?.Game.Services.GetService<IAssetManager>() is not { } assets)
        {
            await LoadFileAsync(_project.Project.ToAbsolutePath(assetPath));
            return;
        }

        try
        {
            IsLoading = true;
            var texture = await assets.LoadAsync<TextureAsset>(assetPath);
            _absolutePath = _project.Project.ToAbsolutePath(assetPath);
            SetImage(texture, _absolutePath);
            ImagePath = assetPath;
        }
        catch (AssetException ex)
        {
            LoadError = $"\"{AssetPath.GetFileName(assetPath)}\" could not be loaded: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedProjectImageChanged(string? value)
    {
        if (value is not null)
            _ = LoadProjectImageAsync(value);
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var start = _project.Project.AssetRoot;
        if (await _files.PickFileToOpenAsync("Choose a tileset image", [ImageFilter], start) is { } path)
            await LoadFileAsync(path);
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private void Import()
    {
        var mapFolder = Path.GetDirectoryName(_project.Project.ToAbsolutePath(_map.Path.Length > 0 ? _map.Path : "map.hexy"))!;
        var source = Path.GetRelativePath(mapFolder, _absolutePath!).Replace('\\', '/');
        var file = TilesetImageFile.FromFile(_absolutePath!, null, source);
        var tileset = Tileset.FromImage(Name.Trim(), _texture!, file, TileWidth, TileHeight, Margin, Spacing);
        Close(tileset);
    }

    [RelayCommand]
    private void Cancel() => Close(null);

    private bool CanImport() => Image is not null && _texture is not null && ValidationMessage is null;

    private void SetImage(TextureAsset texture, string path)
    {
        _texture = texture;
        LoadError = null;
        Image = TileArt.Atlas(Tileset.FromImage("preview", texture, null, 1, 1));
        if (string.IsNullOrWhiteSpace(Name) || Name == _suggestedName)
            Name = _suggestedName = UniqueName(Path.GetFileNameWithoutExtension(path));
    }

    private string? _suggestedName;

    private string UniqueName(string baseName)
    {
        if (baseName.Length == 0)
            return baseName;
        var names = _map.Tilesets.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(baseName))
            return baseName;
        for (var i = 2; ; i++)
        {
            if (!names.Contains($"{baseName} {i}"))
                return $"{baseName} {i}";
        }
    }

    private (int Columns, int Rows) Grid()
    {
        if (Image is null || TileWidth <= 0 || TileHeight <= 0)
            return (0, 0);
        var columns = Math.Max(0, (Image.PixelSize.Width - 2 * Margin + Spacing) / (TileWidth + Spacing));
        var rows = Math.Max(0, (Image.PixelSize.Height - 2 * Margin + Spacing) / (TileHeight + Spacing));
        return (columns, rows);
    }

    [GeneratedRegex(@"Tile size:\s*(\d+)\s*[×xX*]\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex TileSizePattern();

    private static Regex SettingPattern(string name) => new($@"{name}:\s*(\d+)", RegexOptions.IgnoreCase);
}
