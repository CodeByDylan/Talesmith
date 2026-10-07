using Avalonia.Input;
using Talesmith.Assets;

namespace Talesmith.Editor.DragAndDrop;

/// <summary>An asset being dragged, by guid and asset path, such as <c>sprites/hero.png</c>.</summary>
public sealed record DraggedAsset(AssetGuid Guid, string Path)
{
    /// <summary>The lower-case extension including the dot, such as ".png".</summary>
    public string Extension => AssetPath.GetExtension(Path).ToLowerInvariant();
}

/// <summary>What is dragged between editor panels: scene entities from the hierarchy or assets from the asset browser.</summary>
/// <remarks>
/// <para>Entities travel in the in-process <see cref="Format"/>. Assets travel in the application format <see cref="AssetsFormatId"/>, whose value is
/// the assets' guids as 32 hex digits separated by newlines, so any panel or plugin can start an asset drag with <see cref="ForAssets"/>.</para>
/// <para>The hierarchy and the scene viewport accept assets: textures create sprites, <c>.tprefab</c> files instantiate prefabs, <c>.hexy</c> maps
/// create tile map entities and <c>.tparticles</c> presets create particle emitters; inspector asset fields accept assets they can hold.</para>
/// </remarks>
public sealed record EditorDragData
{
    /// <summary>The application data format id of dragged assets.</summary>
    public const string AssetsFormatId = "talesmith.assets";

    /// <summary>The in-process format of dragged entities.</summary>
    public static DataFormat<EditorDragData> Format { get; } = DataFormat.CreateInProcessFormat<EditorDragData>("talesmith.editor-drag");

    /// <summary>The format of dragged assets: guids separated by newlines.</summary>
    public static DataFormat<string> AssetsFormat { get; } = DataFormat.CreateStringApplicationFormat(AssetsFormatId);

    /// <summary>Document ids of dragged entities of the open scene, in hierarchy order.</summary>
    public IReadOnlyList<Guid> Entities { get; init; } = [];

    public IReadOnlyList<DraggedAsset> Assets { get; init; } = [];

    public bool HasEntities => Entities.Count > 0;

    public bool HasAssets => Assets.Count > 0;

    /// <summary>A drag payload with entities, for <c>DragDrop.DoDragDropAsync</c>.</summary>
    public static DataTransfer ForEntities(IEnumerable<Guid> entities)
    {
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(Format, new EditorDragData { Entities = [.. entities] }));
        return transfer;
    }

    /// <summary>A drag payload with assets, with their guids as text for targets outside the editor.</summary>
    public static DataTransfer ForAssets(IEnumerable<AssetGuid> assets)
    {
        var text = string.Join('\n', assets.Where(a => !a.IsEmpty).Select(a => a.ToString()));
        var item = new DataTransferItem();
        item.Set(AssetsFormat, text);
        item.SetText(text);
        var transfer = new DataTransfer();
        transfer.Add(item);
        return transfer;
    }

    /// <summary>Reads dragged entities or assets; assets the catalog does not know are skipped.</summary>
    public static bool TryGet(IDataTransfer? transfer, IAssetCatalog catalog, out EditorDragData data)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        data = null!;
        if (transfer is null)
            return false;
        if (transfer.TryGetValue(Format) is { } entities)
        {
            data = entities;
            return true;
        }

        var assets = new List<DraggedAsset>();
        foreach (var guid in ParseAssets(transfer.TryGetValue(AssetsFormat)))
        {
            if (catalog.TryGetPath(guid, out var path))
                assets.Add(new DraggedAsset(guid, path));
        }

        if (assets.Count == 0)
            return false;
        data = new EditorDragData { Assets = assets };
        return true;
    }

    public static bool TryGet(DragEventArgs e, IAssetCatalog catalog, out EditorDragData data)
    {
        ArgumentNullException.ThrowIfNull(e);
        return TryGet(e.DataTransfer, catalog, out data);
    }

    /// <summary>The distinct guids of an <see cref="AssetsFormat"/> value, skipping lines that are not guids.</summary>
    public static IReadOnlyList<AssetGuid> ParseAssets(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return [];
        var result = new List<AssetGuid>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (AssetGuid.TryParse(line, null, out var guid) && !guid.IsEmpty && !result.Contains(guid))
                result.Add(guid);
        }

        return result;
    }
}
