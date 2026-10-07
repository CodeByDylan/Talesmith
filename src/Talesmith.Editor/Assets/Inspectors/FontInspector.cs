using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using Talesmith.Assets;
using Talesmith.Assets.Database;
using Talesmith.Assets.Fonts;
using Talesmith.Editor.Assets.Thumbnails;

namespace Talesmith.Editor.Assets.Inspectors;

/// <summary>Inspects fonts: sample text at several sizes and <see cref="FontImportSettings"/> with fallback fonts.</summary>
public sealed class FontInspector : IAssetInspector
{
    public IReadOnlyList<AssetKind> Kinds { get; } = [AssetKind.Font];

    public AssetInspection Inspect(AssetInspectionContext context) => new FontInspection(context);
}

/// <summary>A font in a fallback list, or a font that can be added to it.</summary>
public sealed record FontChoice(AssetGuid Guid, string Name)
{
    public override string ToString() => Name;
}

/// <summary>A font's preview and editable <see cref="FontImportSettings"/>.</summary>
public sealed partial class FontInspection : AssetInspection
{
    private static readonly int[] Sizes = [12, 16, 24, 36, 56];

    private FontImportSettings _stored = new();
    private bool _reading;
    private bool _renderQueued;

    [ObservableProperty]
    private Bitmap? _preview;

    partial void OnPreviewChanged(Bitmap? oldValue, Bitmap? newValue) => oldValue?.Dispose();

    [ObservableProperty]
    private string _sampleText = "The quick brown fox jumps over the lazy dog";

    [ObservableProperty]
    private string _fileFamily = "";

    [ObservableProperty]
    private string _familyName = "";

    [ObservableProperty]
    private double _defaultSize = 16;

    [ObservableProperty]
    private FontChoice? _fallbackToAdd;

    public FontInspection(AssetInspectionContext context)
        : base(context)
    {
        RevertCore();
        QueueRender();
    }

    public override bool HasSettings => true;

    public ObservableCollection<FontChoice> Fallbacks { get; } = [];

    /// <summary>The project's other fonts, which can become fallbacks.</summary>
    public IReadOnlyList<FontChoice> AvailableFonts =>
        Context.Project.Database is { } database
            ? [.. database.Assets.Where(a => a.Kind == AssetKind.Font && a.Guid != Asset.Guid && Fallbacks.All(f => f.Guid != a.Guid)).Select(a => new FontChoice(a.Guid, a.Name))]
            : [];

    /// <summary>The color preview text is drawn in, set by the view from the theme.</summary>
    public Color Foreground { get; set; } = Colors.White;

    public override Control CreateView() => new FontInspectorView { DataContext = this };

    public FontImportSettings Edited => _stored with
    {
        FamilyName = string.IsNullOrWhiteSpace(FamilyName) ? null : FamilyName.Trim(),
        DefaultSize = (float)Math.Clamp(DefaultSize, 1, 512),
        Fallbacks = [.. Fallbacks.Select(f => f.Guid)]
    };

    [RelayCommand]
    private void AddFallback()
    {
        if (FallbackToAdd is not { } font)
            return;
        Fallbacks.Add(font);
        FallbackToAdd = null;
        OnPropertyChanged(nameof(AvailableFonts));
        Touch();
    }

    [RelayCommand]
    private void RemoveFallback(FontChoice? font)
    {
        if (font is null)
            return;
        Fallbacks.Remove(font);
        OnPropertyChanged(nameof(AvailableFonts));
        Touch();
    }

    [RelayCommand]
    private void MoveFallbackUp(FontChoice? font)
    {
        var index = font is null ? -1 : Fallbacks.IndexOf(font);
        if (index <= 0)
            return;
        Fallbacks.Move(index, index - 1);
        Touch();
    }

    /// <summary>Draws the preview again, such as after the theme changed.</summary>
    public void QueueRender()
    {
        if (_renderQueued)
            return;
        _renderQueued = true;
        Dispatcher.UIThread.Post(() => _ = RenderAsync(), DispatcherPriority.Background);
    }

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
        try
        {
            _stored = Asset.Meta.GetSettings<FontImportSettings>();
        }
        catch (AssetException)
        {
            _stored = new FontImportSettings();
        }

        _reading = true;
        FamilyName = _stored.FamilyName ?? "";
        DefaultSize = _stored.DefaultSize;
        Fallbacks.Clear();
        foreach (var guid in _stored.Fallbacks)
            Fallbacks.Add(new FontChoice(guid, Context.Project.Database?.TryGetAsset(guid, out var record) == true ? record.Name : guid.ToString()));
        OnPropertyChanged(nameof(AvailableFonts));
        _reading = false;
    }

    protected override void OnContentChanged() => QueueRender();

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(SampleText))
            QueueRender();
        else if (e.PropertyName is nameof(FamilyName) or nameof(DefaultSize))
            Touch();
    }

    private void Touch()
    {
        if (_reading)
            return;
        var edited = Edited;
        IsDirty = !string.Equals(edited.FamilyName, _stored.FamilyName, StringComparison.Ordinal) || !edited.DefaultSize.Equals(_stored.DefaultSize)
                  || !edited.Fallbacks.SequenceEqual(_stored.Fallbacks);
    }

    private async Task RenderAsync()
    {
        _renderQueued = false;
        var path = Context.FullPath;
        var text = string.IsNullOrEmpty(SampleText) ? "Aa" : SampleText;
        var color = new SKColor(Foreground.R, Foreground.G, Foreground.B, Foreground.A);
        var result = await Task.Run(() => Render(path, text, color));
        if (result is { } rendered)
        {
            Preview = rendered.Bitmap;
            FileFamily = rendered.Family;
        }
    }

    private static (Bitmap Bitmap, string Family)? Render(string path, string text, SKColor color)
    {
        using var typeface = SKTypeface.FromFile(path);
        if (typeface is null)
            return null;
        const int width = 900;
        var height = Sizes.Sum(s => (int)(s * 1.45)) + 12;
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        using var label = new SKFont(SKTypeface.Default, 10);
        using var labelPaint = new SKPaint { Color = color.WithAlpha(110), IsAntialias = true };
        float y = 4;
        foreach (var size in Sizes)
        {
            using var font = new SKFont(typeface, size) { Subpixel = true, Edging = SKFontEdging.SubpixelAntialias };
            y += size * 1.2f;
            canvas.DrawText($"{size}", 0, y - size * 0.25f, SKTextAlign.Left, label, labelPaint);
            canvas.DrawText(text, 28, y, SKTextAlign.Left, font, paint);
            y += size * 0.25f;
        }

        var png = SkiaImages.EncodePng(bitmap);
        using var stream = new MemoryStream(png);
        var style = typeface.FontStyle.Weight == (int)SKFontStyleWeight.Normal && typeface.FontStyle.Slant == SKFontStyleSlant.Upright ? "" : $" ({StyleName(typeface.FontStyle)})";
        return (new Bitmap(stream), typeface.FamilyName + style);
    }

    private static string StyleName(SKFontStyle style)
    {
        var weight = style.Weight switch
        {
            >= 800 => "Extra Bold",
            >= 700 => "Bold",
            >= 600 => "Semibold",
            >= 500 => "Medium",
            <= 300 => "Light",
            _ => ""
        };
        var italic = style.Slant == SKFontStyleSlant.Upright ? "" : " Italic";
        return (weight + italic).Trim();
    }
}
