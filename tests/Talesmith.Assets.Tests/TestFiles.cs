using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Talesmith.Assets.Atlases;
using Talesmith.Assets.Fonts;
using Talesmith.Assets.Localization;
using Talesmith.Assets.Materials;
using Talesmith.Assets.Shaders;
using Talesmith.Assets.Textures;
using Talesmith.Events;
using Talesmith.Mathematics;

namespace Talesmith.Assets.Tests;

/// <summary>A temporary asset folder deleted after the test.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder() => Root = Directory.CreateTempSubdirectory("talesmith-assets-tests").FullName;

    public string Root { get; }

    public string AssetRoot => Path.Combine(Root, "assets");

    public string FullPath(string path) => Path.Combine(AssetRoot, path.Replace('/', Path.DirectorySeparatorChar));

    public string Write(string path, string contents)
    {
        var full = FullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, contents);
        return full;
    }

    public string Write(string path, byte[] contents)
    {
        var full = FullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, contents);
        return full;
    }

    public void WriteMeta(string path, AssetMeta meta) => Write(AssetMetaFile.GetMetaPath(path), AssetMetaFile.Serialize(meta));

    public AssetMeta ReadMeta(string path) => AssetMetaFile.Parse(File.ReadAllBytes(FullPath(AssetMetaFile.GetMetaPath(path))));

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

internal static class TestAssets
{
    public static IAssetImporter[] Importers =>
    [
        new TextureImporter(), new SpriteAtlasImporter(), new FontImporter(), new ShaderImporter(), new MaterialImporter(), new StringTableImporter()
    ];

    /// <summary>A PNG whose pixels are <paramref name="color"/> inside <paramref name="opaque"/> and transparent elsewhere.</summary>
    public static byte[] Png(int width, int height, Color color, Rect2? opaque = null)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var area = opaque ?? new Rect2(0, 0, width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var inside = x >= area.X && x < area.Right && y >= area.Y && y < area.Bottom;
                bitmap.SetPixel(x, y, inside ? new SKColor(color.R, color.G, color.B, color.A) : SKColors.Transparent);
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static AssetManager Manager(string root, AssetCatalog? catalog = null, IEventBus? events = null) =>
        new(new FileSystemAssetSource(root), Importers, NullLogger<AssetManager>.Instance, catalog, events: events);
}
