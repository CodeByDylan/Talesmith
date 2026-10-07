using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Assets;
using Talesmith.Assets.Database;
using Talesmith.Assets.Textures;
using Talesmith.Editor.Assets.Controls;
using Talesmith.Editor.Assets.SpriteEditor;
using Talesmith.Imaging;
using Talesmith.Rendering;

namespace Talesmith.Editor.Assets.Inspectors;

/// <summary>Inspects textures: a zoomable preview with their sprites, and their import settings.</summary>
public sealed class TextureInspector : IAssetInspector
{
    public IReadOnlyList<AssetKind> Kinds { get; } = [AssetKind.Texture];

    public AssetInspection Inspect(AssetInspectionContext context) => new TextureInspection(context);
}

/// <summary>A texture's preview and editable <see cref="TextureImportSettings"/>.</summary>
public sealed partial class TextureInspection : AssetInspection
{
    private static readonly int[] MaxSizes = [0, 256, 512, 1024, 2048, 4096, 8192];

    private ImageData? _image;
    private bool _reading;
    private TextureImportSettings _stored = new();

    [ObservableProperty]
    private Bitmap? _preview;

    partial void OnPreviewChanged(Bitmap? oldValue, Bitmap? newValue) => oldValue?.Dispose();

    [ObservableProperty]
    private IReadOnlyList<CanvasRegion>? _regions;

    [ObservableProperty]
    private string _info = "";

    [ObservableProperty]
    private int _filterIndex;

    [ObservableProperty]
    private int _wrapIndex;

    [ObservableProperty]
    private bool _mipmaps;

    [ObservableProperty]
    private bool _premultipliedAlpha;

    [ObservableProperty]
    private int _maxSizeIndex;

    [ObservableProperty]
    private int _compressionIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMultiple))]
    private int _spriteModeIndex;

    [ObservableProperty]
    private bool _showSprites = true;

    public TextureInspection(AssetInspectionContext context)
        : base(context)
    {
        RevertCore();
        _ = LoadAsync();
    }

    public override bool HasSettings => true;

    public IReadOnlyList<string> Filters { get; } = ["Game default", "Nearest (pixel art)", "Linear (smooth)"];

    public IReadOnlyList<string> WrapModes { get; } = ["Clamp", "Repeat", "Mirror"];

    public IReadOnlyList<string> MaxSizeNames { get; } = ["Original", "256", "512", "1024", "2048", "4096", "8192"];

    public IReadOnlyList<string> Compressions { get; } = ["None", "Normal", "High quality"];

    public IReadOnlyList<string> SpriteModes { get; } = ["Single", "Multiple"];

    public bool IsMultiple => SpriteModeIndex == 1;

    public override Control CreateView() => new TextureInspectorView { DataContext = this };

    [RelayCommand]
    private void OpenSpriteEditor() => Context.Services.GetService<SpriteEditorService>()?.Open(Asset);

    /// <summary>The settings as edited.</summary>
    public TextureImportSettings Edited => _stored with
    {
        Filter = FilterIndex switch
        {
            1 => TextureFilter.Nearest,
            2 => TextureFilter.Linear,
            _ => null
        },
        Wrap = (TextureWrapMode)WrapIndex,
        Mipmaps = Mipmaps,
        PremultipliedAlpha = PremultipliedAlpha,
        MaxSize = MaxSizes[Math.Clamp(MaxSizeIndex, 0, MaxSizes.Length - 1)],
        Compression = (TextureCompression)CompressionIndex,
        SpriteMode = (SpriteMode)SpriteModeIndex
    };

    protected override async Task<AssetRecord?> ApplyCoreAsync()
    {
        var edited = Edited;
        var record = await Context.Operations.SetImportSettingsAsync(Asset, edited);
        if (record is not null)
            _stored = edited;
        return record;
    }

    protected override void RevertCore()
    {
        var settings = _stored = Current();
        _reading = true;
        FilterIndex = settings.Filter switch
        {
            TextureFilter.Nearest => 1,
            TextureFilter.Linear => 2,
            _ => 0
        };
        WrapIndex = (int)settings.Wrap;
        Mipmaps = settings.Mipmaps;
        PremultipliedAlpha = settings.PremultipliedAlpha;
        MaxSizeIndex = Math.Max(0, Array.IndexOf(MaxSizes, settings.MaxSize));
        CompressionIndex = (int)settings.Compression;
        SpriteModeIndex = (int)settings.SpriteMode;
        _reading = false;
        UpdateRegions();
    }

    protected override void OnContentChanged() => _ = LoadAsync();

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_reading || e.PropertyName is null)
            return;
        if (e.PropertyName is nameof(FilterIndex) or nameof(WrapIndex) or nameof(Mipmaps) or nameof(PremultipliedAlpha) or nameof(MaxSizeIndex)
            or nameof(CompressionIndex) or nameof(SpriteModeIndex))
        {
            IsDirty = Edited != _stored;
            if (e.PropertyName == nameof(SpriteModeIndex))
                UpdateRegions();
        }
        else if (e.PropertyName == nameof(ShowSprites))
        {
            UpdateRegions();
        }
    }

    private TextureImportSettings Current()
    {
        try
        {
            return Asset.Meta.GetSettings<TextureImportSettings>();
        }
        catch (AssetException)
        {
            return new TextureImportSettings();
        }
    }

    private async Task LoadAsync()
    {
        var path = Context.FullPath;
        try
        {
            var (bitmap, image) = await Task.Run(() =>
            {
                using var stream = File.OpenRead(path);
                var decoded = TextureDecoder.Decode(stream);
                return (new Bitmap(path), decoded);
            });
            Dispatcher.UIThread.Post(() =>
            {
                Preview = bitmap;
                _image = image;
                UpdateRegions();
            });
        }
        catch (Exception ex) when (ex is IOException or AssetException or UnauthorizedAccessException)
        {
            Info = "The image could not be read: " + ex.Message;
        }
    }

    private void UpdateRegions()
    {
        if (_image is not { } image)
            return;
        var settings = Edited;
        var sheet = settings.SpriteMode == SpriteMode.Multiple ? SpriteSlicer.Slice(image, settings, AssetPath.GetFileNameWithoutExtension(Asset.Path)) : null;
        Regions = sheet is null || !ShowSprites ? null : [.. sheet.Sprites.Select(s => new CanvasRegion(new Rect(s.Rect.X, s.Rect.Y, s.Rect.Width, s.Rect.Height), s.Name))];
        var text = $"{image.Width} × {image.Height} px";
        if (sheet is not null)
        {
            text += $"  ·  {sheet.Sprites.Count} {(sheet.Sprites.Count == 1 ? "sprite" : "sprites")}";
            if (sheet.Animations.Count > 0)
                text += $"  ·  {sheet.Animations.Count} {(sheet.Animations.Count == 1 ? "animation" : "animations")}";
        }

        Info = text;
    }
}
