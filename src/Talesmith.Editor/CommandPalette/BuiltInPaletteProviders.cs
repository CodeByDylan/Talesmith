using Talesmith.Assets;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Panels;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Selection;
using Talesmith.Editor.Viewport;
using Talesmith.UI;

namespace Talesmith.Editor.CommandPalette;

/// <summary>Goes to an entity of the open scene: selects it and frames it in the viewport. Prefix: @.</summary>
public sealed class EntityPaletteProvider(ISceneDocumentService documents, ISelectionService selection, ViewportService viewport, LayoutService layout)
    : ICommandPaletteProvider
{
    public char? Prefix => '@';

    public string Category => "Entities";

    public IEnumerable<PaletteItem> Search(string query, int limit)
    {
        if (documents.Active is not { } model)
            return [];
        return model.Entities
            .Select(e => (Entity: e, Score: FuzzyMatch.Score(string.IsNullOrEmpty(e.Name) ? "Entity" : e.Name, query)))
            .Where(m => m.Score > 0)
            .OrderByDescending(m => m.Score)
            .Take(limit)
            .Select(m => new PaletteItem($"entity:{m.Entity.Id:N}", string.IsNullOrEmpty(m.Entity.Name) ? "Entity" : m.Entity.Name, Category, () => GoTo(m.Entity.Id))
            {
                Icon = Icons.Box,
                Subtitle = PathOf(model, m.Entity.Parent),
                Score = m.Score
            });
    }

    private static string? PathOf(SceneDocumentModel model, Guid? parent)
    {
        var names = new List<string>();
        for (var current = parent is { } id ? model.Find(id) : null; current is not null; current = current.Parent is { } p ? model.Find(p) : null)
            names.Insert(0, current.Name);
        return names.Count == 0 ? null : string.Join(" / ", names);
    }

    private void GoTo(Guid id)
    {
        selection.SelectEntity(id);
        layout.ShowPanel(PanelIds.Scene);
        viewport.FrameSelection();
    }
}

/// <summary>Opens an asset: scenes open in the viewport, other assets are selected. Prefix: #.</summary>
public sealed class AssetPaletteProvider(IProjectService project, ISceneDocumentService documents, ISelectionService selection, LayoutService layout)
    : ICommandPaletteProvider
{
    public char? Prefix => '#';

    public string Category => "Assets";

    public IEnumerable<PaletteItem> Search(string query, int limit)
    {
        if (project.Database is not { IsScanned: true } database)
            return [];
        return database.Assets
            .Where(a => !a.IsFolder)
            .Select(a => (Asset: a, Score: Math.Max(FuzzyMatch.Score(a.Name, query), FuzzyMatch.Score(a.Path, query) * 0.7)))
            .Where(m => m.Score > 0)
            .OrderByDescending(m => m.Score)
            .Take(limit)
            .Select(m => new PaletteItem($"asset:{m.Asset.Guid}", m.Asset.Name, Category, () => Open(m.Asset.Guid, m.Asset.Kind))
            {
                Icon = Icons.Find(m.Asset.Kind.Icon) ?? Icons.File,
                Subtitle = AssetPath.GetDirectory(m.Asset.Path) is { Length: > 0 } folder ? folder : null,
                Score = m.Score
            });
    }

    private void Open(AssetGuid guid, AssetKind kind)
    {
        if (kind == AssetKind.Scene)
        {
            _ = documents.OpenAsync(guid);
            return;
        }

        selection.SelectAsset(guid);
        layout.ShowPanel(PanelIds.Assets);
    }
}
