using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Input;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Assets;
using Talesmith.Editor.DragAndDrop;
using Talesmith.Editor.Hierarchy;
using Talesmith.Editor.Prefabs;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Selection;
using Talesmith.Editor.Viewport.Gizmos;
using Talesmith.Runtime.Serialization;
using Talesmith.UI;

namespace Talesmith.Editor.Inspector.Editors;

/// <summary>Asset references as a field showing the asset's icon and name: click to pick from the project's matching assets, drop an asset from
/// the asset browser, ping it or clear it.</summary>
public sealed class AssetEditorProvider : IPropertyEditorProvider
{
    public Control? CreateEditor(PropertyEditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Property.Kind == PropertyKind.Asset ? AssetField.Create(context.Property, context.Value, context.Services) : null;
    }
}

/// <summary>Entity references as a field: pick from the scene's entities, drop one from the hierarchy or pick it in the viewport with the eyedropper.</summary>
public sealed class EntityEditorProvider : IPropertyEditorProvider
{
    public Control? CreateEditor(PropertyEditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Property.Kind == PropertyKind.Entity ? EntityField.Create(context.Value, context.Services) : null;
    }
}

/// <summary>Which assets a property can hold.</summary>
public static class AssetFilters
{
    /// <summary>The extensions an asset property accepts, or empty for any file.</summary>
    public static IReadOnlyList<string> Extensions(PropertyDescriptor property)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (property.AssetExtensions.Count > 0)
            return [.. property.AssetExtensions.Select(e => e.ToLowerInvariant())];
        var kind = KindOf(property.AssetType);
        return kind is null ? [] : AssetKindRegistry.Default.GetExtensions(kind);
    }

    /// <summary>The asset kind of a runtime asset type, by its name, or null when any file may do.</summary>
    public static AssetKind? KindOf(Type? type)
    {
        var name = type?.Name ?? "";
        return name switch
        {
            _ when name.Contains("Texture", StringComparison.Ordinal) => AssetKind.Texture,
            _ when name.Contains("TileMap", StringComparison.Ordinal) => AssetKind.TileMap,
            "SoundClip" or "MusicTrack" => AssetKind.Audio,
            "PrefabDocument" => AssetKind.Prefab,
            "ParticlePreset" => AssetKind.ParticleSystem,
            "SceneDocument" => AssetKind.Scene,
            _ when name.Contains("Font", StringComparison.Ordinal) => AssetKind.Font,
            _ when name.Contains("Material", StringComparison.Ordinal) => AssetKind.Material,
            _ => null
        };
    }

    public static bool Accepts(IReadOnlyList<string> extensions, string path) =>
        extensions.Count == 0 || extensions.Contains(AssetPath.GetExtension(path).ToLowerInvariant());

    /// <summary>The brush resource for an asset kind's icon.</summary>
    public static string BrushOf(AssetKind kind) =>
        kind == AssetKind.Texture || kind == AssetKind.Atlas ? "InfoBrush"
        : kind == AssetKind.Prefab || kind == AssetKind.Script ? "SuccessBrush"
        : kind == AssetKind.Audio ? "DangerBrush"
        : kind == AssetKind.ParticleSystem || kind == AssetKind.Scene ? "AccentBrush"
        : kind == AssetKind.TileMap ? "WarningBrush"
        : "TextSecondaryBrush";
}

internal static class AssetField
{
    public static Control Create(PropertyDescriptor property, IPropertyValue value, IServiceProvider services)
    {
        var project = services.GetRequiredService<IProjectService>();
        var selection = services.GetRequiredService<ISelectionService>();
        var extensions = AssetFilters.Extensions(property);
        var kind = AssetFilters.KindOf(property.AssetType) ?? (extensions.Count > 0 ? AssetKindRegistry.Default.Classify("x" + extensions[0]) : AssetKind.Other);
        var box = new FieldBox();
        var ping = box.AddAction(Icons.Crosshair, "Select the asset in the asset browser", () =>
        {
            if (JsonValues.Asset(value.Get()) is { } guid)
                selection.SelectAsset(guid);
        });
        var clear = box.AddAction(Icons.X, "Clear", () => value.Set(null));
        box.Main.Flyout = PickerList.Attach(box, query => Items(project, extensions, query), item => value.Set(item.Value is AssetGuid guid ? guid.ToString() : null),
            $"Search {(kind == AssetKind.Other ? "assets" : kind.DisplayName.ToLowerInvariant() + "s")}");

        DragDrop.SetAllowDrop(box, true);
        DragDrop.AddDragOverHandler(box, (_, e) =>
        {
            var accepts = EditorDragData.TryGet(e, project.Catalog, out var data) && data.Assets.Any(a => AssetFilters.Accepts(extensions, a.Path)) && !value.IsReadOnly;
            e.DragEffects = accepts ? DragDropEffects.Link : DragDropEffects.None;
            box.SetDropTarget(accepts);
            e.Handled = true;
        });
        DragDrop.AddDragLeaveHandler(box, (_, _) => box.SetDropTarget(false));
        DragDrop.AddDropHandler(box, (_, e) =>
        {
            box.SetDropTarget(false);
            if (EditorDragData.TryGet(e, project.Catalog, out var data) && data.Assets.FirstOrDefault(a => AssetFilters.Accepts(extensions, a.Path)) is { } asset)
            {
                value.Set(asset.Guid.ToString());
                e.Handled = true;
            }
        });

        return PropertyEditors.Watch(box, value, () =>
        {
            var guid = JsonValues.Asset(value.Get());
            ping.IsVisible = guid is not null;
            clear.IsVisible = guid is not null && !value.IsReadOnly;
            if (value.IsMixed)
            {
                box.Show(Icons.Find(kind.Icon) ?? Icons.File, "—", AssetFilters.BrushOf(kind), muted: true);
                return;
            }

            if (guid is not { } id)
            {
                box.Show(Icons.Find(kind.Icon) ?? Icons.File, kind == AssetKind.Other ? "None" : $"None ({kind.DisplayName})", "TextMutedBrush", muted: true);
                return;
            }

            if (!project.Catalog.TryGetPath(id, out var path))
            {
                box.Show(Icons.AlertTriangle, "Missing asset", "DangerBrush", muted: false, error: true);
                ToolTip.SetTip(box.Main, $"No asset in the project has the guid {id}.");
                return;
            }

            var assetKind = project.Database?.Kinds.Classify(path) ?? AssetKindRegistry.Default.Classify(path);
            box.Show(Icons.Find(assetKind.Icon) ?? Icons.File, AssetPath.GetFileName(path), AssetFilters.BrushOf(assetKind), muted: false);
            ToolTip.SetTip(box.Main, path);
        });
    }

    private static IEnumerable<PickerItem> Items(IProjectService project, IReadOnlyList<string> extensions, string query)
    {
        yield return new PickerItem("None", Icons.X, null, null) { IconBrush = "TextMutedBrush" };
        if (project.Database is not { } database)
            yield break;
        var matches = database.Assets
            .Where(a => !a.IsFolder && AssetFilters.Accepts(extensions, a.Path) && (query.Length == 0 || a.Path.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(a => query.Length > 0 && !a.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Take(200);
        foreach (var asset in matches)
        {
            var folder = AssetPath.GetDirectory(asset.Path);
            yield return new PickerItem(asset.Name, Icons.Find(asset.Kind.Icon) ?? Icons.File, folder.Length == 0 ? "assets" : $"assets/{folder}", asset.Guid)
            {
                IconBrush = AssetFilters.BrushOf(asset.Kind)
            };
        }
    }
}

internal static class EntityField
{
    public static Control Create(IPropertyValue value, IServiceProvider services)
    {
        var entities = services.GetRequiredService<EntityDataService>();
        var documents = services.GetRequiredService<Documents.ISceneDocumentService>();
        var selection = services.GetRequiredService<ISelectionService>();
        var icons = services.GetRequiredService<EntityIcons>();
        var catalog = services.GetRequiredService<IProjectService>().Catalog;
        var gizmos = services.GetService<GizmoLayer>();
        var box = new FieldBox();
        var go = box.AddAction(Icons.ArrowRight, "Select the entity", () =>
        {
            if (JsonValues.EntityId(value.Get()) is { } id)
                selection.SelectEntity(id);
        });
        var pick = box.AddAction(Icons.Pipette, "Pick an entity in the viewport", () =>
            gizmos?.BeginPick(id => value.Set(id is { } picked ? JsonFormats.FormatGuid(picked) : null)));
        pick.IsVisible = gizmos is not null;
        var clear = box.AddAction(Icons.X, "Clear", () => value.Set(null));
        box.Main.Flyout = PickerList.Attach(box, query => Items(documents, icons, query), item =>
            value.Set(item.Value is Guid id ? JsonValue.Create(JsonFormats.FormatGuid(id)) : null), "Search entities");

        DragDrop.SetAllowDrop(box, true);
        DragDrop.AddDragOverHandler(box, (_, e) =>
        {
            var accepts = EditorDragData.TryGet(e, catalog, out var data) && data.HasEntities && !value.IsReadOnly;
            e.DragEffects = accepts ? DragDropEffects.Link : DragDropEffects.None;
            box.SetDropTarget(accepts);
            e.Handled = true;
        });
        DragDrop.AddDragLeaveHandler(box, (_, _) => box.SetDropTarget(false));
        DragDrop.AddDropHandler(box, (_, e) =>
        {
            box.SetDropTarget(false);
            if (EditorDragData.TryGet(e, catalog, out var data) && data.HasEntities)
            {
                value.Set(JsonFormats.FormatGuid(data.Entities[0]));
                e.Handled = true;
            }
        });

        return PropertyEditors.Watch(box, value, () =>
        {
            var id = JsonValues.EntityId(value.Get());
            go.IsVisible = id is not null;
            clear.IsVisible = id is not null && !value.IsReadOnly;
            pick.IsVisible = gizmos is not null && !value.IsReadOnly;
            if (value.IsMixed)
                box.Show(Icons.Box, "—", "TextMutedBrush", muted: true);
            else if (id is not { } target)
                box.Show(Icons.Box, "None (Entity)", "TextMutedBrush", muted: true);
            else if (!entities.Exists(target))
                box.Show(Icons.AlertTriangle, "Missing entity", "DangerBrush", muted: false, error: true);
            else
                box.Show(icons.Get(entities.GetComponents(target)), entities.GetName(target), "TextSecondaryBrush", muted: false);
        });
    }

    private static IEnumerable<PickerItem> Items(Documents.ISceneDocumentService documents, EntityIcons icons, string query)
    {
        yield return new PickerItem("None", Icons.X, null, null) { IconBrush = "TextMutedBrush" };
        if (documents.Active is not { } model)
            yield break;
        var count = 0;
        foreach (var entity in model.Entities)
        {
            var name = EntityDataService.DisplayName(entity.Name);
            if (query.Length > 0 && !name.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;
            var parent = entity.Parent is { } id && model.Find(id) is { } owner ? EntityDataService.DisplayName(owner.Name) : null;
            yield return new PickerItem(name, icons.Get(entity.Components, entity.Prefab is not null), parent, entity.Id);
            if (++count >= 300)
                yield break;
        }
    }
}
