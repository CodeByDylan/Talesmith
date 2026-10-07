using Avalonia.Media;
using Avalonia.Media.Immutable;
using Talesmith.Assets;
using Talesmith.Assets.Database;
using Talesmith.UI;

namespace Talesmith.Editor.Assets;

/// <summary>How an asset kind looks in the editor: its color, icon and the short label on its badge.</summary>
public sealed record AssetKindStyle(Color Color, Geometry Icon, string Badge)
{
    private static readonly Dictionary<string, (string Color, string? Badge)> Known = new(StringComparer.Ordinal)
    {
        [AssetKind.Texture.Id] = ("#3B82F6", null),
        [AssetKind.Atlas.Id] = ("#06B6D4", "ATLAS"),
        [AssetKind.Font.Id] = ("#A855F7", null),
        [AssetKind.Audio.Id] = ("#EF4444", null),
        [AssetKind.Scene.Id] = ("#7073F6", "SCENE"),
        [AssetKind.Prefab.Id] = ("#22C55E", "PREFAB"),
        [AssetKind.Material.Id] = ("#EC4899", "MAT"),
        [AssetKind.Shader.Id] = ("#14B8A6", "SHADER"),
        [AssetKind.ParticleSystem.Id] = ("#D946EF", "VFX"),
        [AssetKind.TileMap.Id] = ("#84CC16", "MAP"),
        [AssetKind.Script.Id] = ("#10B981", "C#"),
        [AssetKind.PluginManifest.Id] = ("#F97316", "PLUGIN"),
        [AssetKind.Localization.Id] = ("#0EA5E9", "LOC"),
        [AssetKind.Folder.Id] = ("#F59E0B", null),
        [AssetKind.Other.Id] = ("#8A909C", null)
    };

    private static readonly Dictionary<string, AssetKindStyle> Cache = new(StringComparer.Ordinal);

    public IBrush Brush { get; } = new ImmutableSolidColorBrush(Color);

    /// <summary>A translucent version of the color for backgrounds.</summary>
    public IBrush SubtleBrush { get; } = new ImmutableSolidColorBrush(Color, 0.14);

    /// <summary>The style of a kind; kinds added by plugins get a color derived from their id.</summary>
    public static AssetKindStyle Of(AssetKind kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        lock (Cache)
        {
            if (Cache.TryGetValue(kind.Id, out var style))
                return style;
            var color = Known.TryGetValue(kind.Id, out var known) ? Color.Parse(known.Color) : Derived(kind.Id);
            var badge = known.Badge ?? (kind.Id == AssetKind.Folder.Id ? "" : kind.DisplayName.ToUpperInvariant());
            style = new AssetKindStyle(color, Icons.Find(kind.Icon) ?? Icons.File, badge);
            Cache[kind.Id] = style;
            return style;
        }
    }

    /// <summary>The badge for one asset: the file extension for images, sounds and fonts, such as "PNG", otherwise the kind's badge.</summary>
    public static string BadgeOf(AssetRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.IsFolder)
            return "";
        if (record.Kind == AssetKind.Texture || record.Kind == AssetKind.Audio || record.Kind == AssetKind.Font || record.Kind == AssetKind.Other)
        {
            var extension = AssetPath.GetExtension(record.Path).TrimStart('.');
            if (extension.Length is > 0 and <= 5)
                return extension.ToUpperInvariant();
        }

        return Of(record.Kind).Badge;
    }

    private static Color Derived(string id)
    {
        var hash = 0u;
        foreach (var c in id)
            hash = hash * 31 + c;
        return HsvColor.ToRgb(hash % 360, 0.6, 0.85);
    }
}
