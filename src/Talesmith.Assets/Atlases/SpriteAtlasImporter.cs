using System.Numerics;
using Microsoft.Extensions.Logging;
using Talesmith.Assets.Json;
using Talesmith.Assets.Textures;
using Talesmith.Imaging;
using Talesmith.Mathematics;

namespace Talesmith.Assets.Atlases;

/// <summary>Packs the textures listed in a <c>.tatlas</c> file into one <see cref="TextureAsset"/> whose sprites are the packed regions.</summary>
/// <remarks>
/// <para>A source texture without sprites becomes one sprite named after its file; a texture with sprites contributes each of them under
/// its own name, together with its animations. Identical images are stored once and shared by every sprite showing them. Sprites are
/// never rotated.</para>
/// <para>The atlas's .meta settings choose filtering and wrapping like a texture's; its sprite settings are ignored. The source textures
/// stay loaded while the atlas is, and reloading one of them rebuilds the atlas.</para>
/// </remarks>
public sealed partial class SpriteAtlasImporter : AssetImporter<TextureAsset, TextureImportSettings>
{
    public const string ImporterId = "atlas";

    private static readonly HashSet<string> TextureExtensions = new(new TextureImporter().Extensions, StringComparer.OrdinalIgnoreCase);

    public override IReadOnlyList<string> Extensions { get; } = [".tatlas"];

    public override string Id => ImporterId;

    public override async Task<TextureAsset> ImportAsync(AssetImportContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        SpriteAtlasDefinition definition;
        var stream = context.OpenRead();
        await using (stream.ConfigureAwait(false))
            definition = await AssetJson.DeserializeAsync<SpriteAtlasDefinition>(stream, "The sprite atlas", cancellationToken).ConfigureAwait(false);
        if (definition.Version > SpriteAtlasDefinition.CurrentVersion)
            throw new AssetException($"The sprite atlas has version {definition.Version}, but this version of Talesmith reads up to {SpriteAtlasDefinition.CurrentVersion}.");

        var textures = new List<TextureAsset>();
        foreach (var path in ResolveSources(context, definition))
            textures.Add(await context.Assets.LoadAsync<TextureAsset>(path, cancellationToken).ConfigureAwait(false));

        var regions = CollectRegions(context, textures, definition.Trim);
        var (image, sprites) = Pack(regions, new AtlasPackOptions(definition.Padding, definition.MaxSize, definition.PowerOfTwo));
        var animations = CollectAnimations(context, textures, sprites);
        return new TextureAsset(context.Path, image) { Sprites = sprites, Animations = animations, Settings = context.GetSettings<TextureImportSettings>() };
    }

    private static List<string> ResolveSources(AssetImportContext context, SpriteAtlasDefinition definition)
    {
        var paths = new List<string>();
        var seen = new HashSet<string>(AssetPath.Comparer);
        foreach (var source in definition.Sources)
        {
            if (string.IsNullOrWhiteSpace(source))
                continue;

            var path = AssetGuid.TryParse(source, null, out var guid) ? context.ResolveGuid(guid) : context.Resolve(source);
            IEnumerable<string> files = context.Source.Exists(path)
                ? [path]
                : context.Source.List(path, "*", recursive: true)
                    .Where(file => TextureExtensions.Contains(AssetPath.GetExtension(file)))
                    .Order(StringComparer.Ordinal);
            var any = false;
            foreach (var file in files)
            {
                any = true;
                if (seen.Add(AssetPath.Normalize(file)))
                    paths.Add(AssetPath.Normalize(file));
            }

            if (!any)
                LogEmptySource(context.Logger, context.Path, source);
        }

        return paths;
    }

    private static List<Region> CollectRegions(AssetImportContext context, List<TextureAsset> textures, bool trim)
    {
        var regions = new List<Region>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var texture in textures)
        {
            var bounds = new PixelRect(0, 0, texture.Width, texture.Height);
            IEnumerable<SpriteSlice> slices = texture.Sprites.Count > 0
                ? texture.Sprites
                : [new SpriteSlice(AssetPath.GetFileNameWithoutExtension(texture.Path), new Rect2(0, 0, texture.Width, texture.Height), new Vector2(0.5f))];
            foreach (var slice in slices)
            {
                if (!names.Add(slice.Name))
                {
                    LogDuplicateName(context.Logger, context.Path, slice.Name, texture.Path);
                    continue;
                }

                var rect = new PixelRect((int)slice.Rect.X, (int)slice.Rect.Y, (int)MathF.Ceiling(slice.Rect.Width), (int)MathF.Ceiling(slice.Rect.Height)).Clip(bounds);
                if (rect.IsEmpty)
                    continue;

                var content = trim ? PixelRegion.FindOpaqueBounds(texture.Image, rect) : rect;
                if (content.IsEmpty)
                    content = rect with { Width = 1, Height = 1 };

                var anchor = new Vector2(rect.X + slice.Pivot.X * rect.Width, rect.Y + slice.Pivot.Y * rect.Height);
                var pivot = new Vector2((anchor.X - content.X) / content.Width, (anchor.Y - content.Y) / content.Height);
                regions.Add(new Region(slice.Name, texture.Image, content, pivot));
            }
        }

        return regions;
    }

    private static (ImageData Image, IReadOnlyList<SpriteSlice> Sprites) Pack(List<Region> regions, AtlasPackOptions options)
    {
        var unique = new List<Region>();
        var uniqueIndex = new int[regions.Count];
        var byHash = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < regions.Count; i++)
        {
            var hash = PixelRegion.Hash(regions[i].Image, regions[i].Rect);
            if (!byHash.TryGetValue(hash, out var index))
            {
                index = unique.Count;
                byHash.Add(hash, index);
                unique.Add(regions[i]);
            }

            uniqueIndex[i] = index;
        }

        var layout = AtlasLayout.Pack([.. unique.Select(region => (region.Rect.Width, region.Rect.Height))], options);
        var pixels = new byte[layout.Width * layout.Height * 4];
        for (var i = 0; i < unique.Count; i++)
            PixelRegion.Copy(unique[i].Image, unique[i].Rect, pixels, layout.Width * 4, layout.Placements[i].X, layout.Placements[i].Y);

        var sprites = new SpriteSlice[regions.Count];
        for (var i = 0; i < regions.Count; i++)
        {
            var placed = layout.Placements[uniqueIndex[i]];
            sprites[i] = new SpriteSlice(regions[i].Name, new Rect2(placed.X, placed.Y, placed.Width, placed.Height), regions[i].Pivot);
        }

        return (new ImageData(layout.Width, layout.Height, pixels), sprites);
    }

    private static List<SpriteAnimationInfo> CollectAnimations(AssetImportContext context, List<TextureAsset> textures, IReadOnlyList<SpriteSlice> sprites)
    {
        var names = sprites.Select(sprite => sprite.Name).ToHashSet(StringComparer.Ordinal);
        var animations = new List<SpriteAnimationInfo>();
        var animationNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var animation in textures.SelectMany(texture => texture.Animations))
        {
            if (!animationNames.Add(animation.Name))
            {
                LogDuplicateAnimation(context.Logger, context.Path, animation.Name);
                continue;
            }

            if (animation.Frames.All(names.Contains))
                animations.Add(animation);
        }

        return animations;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Path}: the atlas source {Source} contains no textures")]
    private static partial void LogEmptySource(ILogger logger, string path, string source);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Path}: sprite \"{Name}\" from {Texture} has the same name as an earlier sprite, so it was left out")]
    private static partial void LogDuplicateName(ILogger logger, string path, string name, string texture);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Path}: animation \"{Name}\" is defined by more than one source texture; the first one is used")]
    private static partial void LogDuplicateAnimation(ILogger logger, string path, string name);

    private sealed record Region(string Name, ImageData Image, PixelRect Rect, Vector2 Pivot);
}
