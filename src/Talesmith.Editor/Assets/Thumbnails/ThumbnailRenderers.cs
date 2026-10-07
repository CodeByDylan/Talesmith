using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Talesmith.Assets;
using Talesmith.Assets.Atlases;
using Talesmith.Assets.Database;
using Talesmith.Assets.Json;
using Talesmith.Assets.Textures;
using Talesmith.Editor.Assets.Previews;
using Talesmith.Editor.Projects;
using Talesmith.Imaging;

namespace Talesmith.Editor.Assets.Thumbnails;

/// <summary>Draws thumbnails of one kind of asset, such as textures or sounds; called on a thread pool thread.</summary>
/// <remarks>Register with <see cref="ThumbnailServiceCollectionExtensions.AddThumbnailRenderer{T}"/>; the last renderer that can draw
/// an asset is used. Assets without a renderer show their kind's icon.</remarks>
public interface IThumbnailRenderer
{
    bool CanRender(AssetRecord asset);

    /// <summary>Draws the thumbnail as a PNG that fits in a square of <paramref name="size"/> pixels; null when there is nothing to show.</summary>
    /// <param name="file">The asset's absolute path.</param>
    byte[]? Render(string file, AssetRecord asset, int size, CancellationToken cancellationToken);
}

public static class ThumbnailServiceCollectionExtensions
{
    public static IServiceCollection AddThumbnailRenderer<T>(this IServiceCollection services)
        where T : class, IThumbnailRenderer
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IThumbnailRenderer, T>();
        return services;
    }
}

/// <summary>Textures show their first sprite (the first frame of their first animation, if any), or the whole image.</summary>
public sealed class TextureThumbnailRenderer : IThumbnailRenderer
{
    public bool CanRender(AssetRecord asset) => asset.Kind == AssetKind.Texture;

    public byte[]? Render(string file, AssetRecord asset, int size, CancellationToken cancellationToken)
    {
        ImageData image;
        using (var stream = File.OpenRead(file))
            image = TextureDecoder.Decode(stream);
        cancellationToken.ThrowIfCancellationRequested();
        var region = PreviewRegion(image, asset);
        using var bitmap = SkiaImages.ToBitmap(image);
        using var fitted = SkiaImages.Fit(bitmap, region, size);
        return SkiaImages.EncodePng(fitted);
    }

    /// <summary>The part of a texture that represents it: its first sprite or animation frame for sprite sheets, else all of it.</summary>
    internal static SKRectI PreviewRegion(ImageData image, AssetRecord asset)
    {
        var whole = new SKRectI(0, 0, image.Width, image.Height);
        TextureImportSettings settings;
        try
        {
            settings = asset.Meta.GetSettings<TextureImportSettings>();
        }
        catch (AssetException)
        {
            return whole;
        }

        if (settings.SpriteMode != SpriteMode.Multiple)
            return whole;
        var sheet = SpriteSlicer.Slice(image, settings, AssetPath.GetFileNameWithoutExtension(asset.Path));
        if (sheet.Sprites.Count == 0)
            return whole;
        var name = sheet.Animations.Count > 0 && sheet.Animations[0].Frames.Count > 0 ? sheet.Animations[0].Frames[0] : null;
        var sprite = sheet.Sprites.FirstOrDefault(s => s.Name == name) ?? sheet.Sprites[0];
        var rect = new SKRectI((int)sprite.Rect.X, (int)sprite.Rect.Y, (int)MathF.Ceiling(sprite.Rect.Right), (int)MathF.Ceiling(sprite.Rect.Bottom));
        rect.Intersect(whole);
        return rect.IsEmpty ? whole : rect;
    }
}

/// <summary>Fonts show "Aa" in the font.</summary>
public sealed class FontThumbnailRenderer : IThumbnailRenderer
{
    public bool CanRender(AssetRecord asset) => asset.Kind == AssetKind.Font;

    public byte[]? Render(string file, AssetRecord asset, int size, CancellationToken cancellationToken)
    {
        using var typeface = SKTypeface.FromFile(file);
        if (typeface is null)
            return null;
        using var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        using var font = new SKFont(typeface, size * 0.46f) { Subpixel = true, Edging = SKFontEdging.Antialias };
        using var paint = new SKPaint { Color = ToSkia(AssetKindStyle.Of(AssetKind.Font).Color), IsAntialias = true };
        const string text = "Aa";
        var width = font.MeasureText(text, out var bounds, paint);
        canvas.DrawText(text, (size - width) / 2, size / 2f - bounds.MidY, SKTextAlign.Left, font, paint);
        return SkiaImages.EncodePng(bitmap);
    }

    internal static SKColor ToSkia(global::Avalonia.Media.Color color) => new(color.R, color.G, color.B, color.A);
}

/// <summary>Sounds show their waveform.</summary>
public sealed class AudioThumbnailRenderer : IThumbnailRenderer
{
    public bool CanRender(AssetRecord asset) => asset.Kind == AssetKind.Audio;

    public byte[]? Render(string file, AssetRecord asset, int size, CancellationToken cancellationToken)
    {
        var waveform = Waveform.Read(file, cancellationToken);
        if (waveform.Frames == 0)
            return null;
        var height = (int)(size * 0.62f);
        using var bitmap = new SKBitmap(new SKImageInfo(size, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { Color = FontThumbnailRenderer.ToSkia(AssetKindStyle.Of(AssetKind.Audio).Color), IsAntialias = true, StrokeCap = SKStrokeCap.Round };
        const int bar = 3;
        const int gap = 2;
        var bars = size / (bar + gap);
        var middle = height / 2f;
        for (var i = 0; i < bars; i++)
        {
            var (min, max) = waveform.Range(i / (double)bars, (i + 1) / (double)bars);
            var amplitude = Math.Max(Math.Max(-min, max), 0.02f) * middle * 0.95f;
            var x = i * (bar + gap) + (size - bars * (bar + gap) + gap) / 2f;
            canvas.DrawRoundRect(SKRect.Create(x, middle - amplitude, bar, amplitude * 2), bar / 2f, bar / 2f, paint);
        }

        return SkiaImages.EncodePng(bitmap);
    }
}

/// <summary>Sprite atlases show the first sprites of their sources in a two by two mosaic.</summary>
public sealed class AtlasThumbnailRenderer(IProjectService project) : IThumbnailRenderer
{
    private const int Cells = 2;

    public bool CanRender(AssetRecord asset) => asset.Kind == AssetKind.Atlas;

    public byte[]? Render(string file, AssetRecord asset, int size, CancellationToken cancellationToken)
    {
        if (project.Database is not { } database)
            return null;
        var definition = AssetJson.Deserialize<SpriteAtlasDefinition>(File.ReadAllBytes(file), "The sprite atlas");
        var textures = Textures(database, asset, definition).Take(Cells * Cells).ToList();
        if (textures.Count == 0)
            return null;
        using var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        var cell = size / Cells;
        for (var i = 0; i < textures.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var texture = textures[i];
            ImageData image;
            using (var stream = File.OpenRead(Path.Combine(database.RootFolder, texture.Path.Replace('/', Path.DirectorySeparatorChar))))
                image = TextureDecoder.Decode(stream);
            using var source = SkiaImages.ToBitmap(image);
            using var fitted = SkiaImages.Fit(source, TextureThumbnailRenderer.PreviewRegion(image, texture), cell - 8);
            var x = i % Cells * cell + (cell - fitted.Width) / 2;
            var y = i / Cells * cell + (cell - fitted.Height) / 2;
            canvas.DrawBitmap(fitted, x, y);
        }

        return SkiaImages.EncodePng(bitmap);
    }

    private static IEnumerable<AssetRecord> Textures(AssetDatabase database, AssetRecord atlas, SpriteAtlasDefinition definition)
    {
        foreach (var source in definition.Sources)
        {
            AssetRecord? record;
            if (AssetGuid.TryParse(source, null, out var guid))
                database.TryGetAsset(guid, out record);
            else
                database.TryGetAsset(AssetPath.Combine(AssetPath.GetDirectory(atlas.Path), source), out record);
            if (record is null)
                continue;
            if (!record.IsFolder)
            {
                if (record.Kind == AssetKind.Texture)
                    yield return record;
                continue;
            }

            foreach (var texture in database.Assets.Where(a => a.Kind == AssetKind.Texture && AssetPath.IsWithin(a.Path, record.Path)).OrderBy(a => a.Path, StringComparer.Ordinal))
                yield return texture;
        }
    }
}
