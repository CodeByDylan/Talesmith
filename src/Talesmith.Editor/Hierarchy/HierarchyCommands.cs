using Talesmith.Editor.Commands;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Selection;
using Talesmith.UI;

namespace Talesmith.Editor.Hierarchy;

/// <summary>The hierarchy's commands: copy and paste, rename, select children and creating the 2D objects the GameObject menu does not have yet.</summary>
public sealed class HierarchyCommands(HierarchyViewModel hierarchy, ISceneDocumentService documents, ISelectionService selection, EntityClipboard clipboard,
    EntityTemplates templates) : IEditorCommandContributor
{
    private static readonly HashSet<string> Existing = new(StringComparer.Ordinal) { "sprite", "camera" };

    void IEditorCommandContributor.Contribute(CommandBuilder builder)
    {
        const string Category = "GameObject";
        bool HasSelection() => documents.Active is { } model && selection.Entities.Any(model.Contains) && !hierarchy.IsLive;
        bool CanPaste() => documents.Active is not null && !hierarchy.IsLive;

        builder.Add("entity.copy", "Copy", "Edit", () => _ = CopyAsync(), HasSelection, "Ctrl+C", Icons.Copy,
            "Copies the selected entities with their children to the clipboard.");
        builder.Add("entity.paste", "Paste", "Edit", () => _ = PasteAsync(), CanPaste, "Ctrl+V", Icons.Paste,
            "Pastes copied entities next to the selected entity.");
        builder.Add("entity.rename", "Rename", "Edit", () => hierarchy.BeginRename(), HasSelection, "F2", Icons.PenLine, "Renames the selected entity.");
        builder.Add("entity.selectChildren", "Select children", "Edit", hierarchy.SelectChildren, HasSelection, null, Icons.ListTree,
            "Selects the children of the selected entities.");
        builder.KeepOutOfText("entity.copy", "entity.paste", "entity.rename");
        builder.Menu(MenuPaths.Edit, "entity.copy", "clipboard");
        builder.Menu(MenuPaths.Edit, "entity.paste", "clipboard");
        builder.Menu(MenuPaths.Edit, "entity.rename", "entities");
        builder.Menu(MenuPaths.Edit, "entity.selectChildren", "selection");

        var order = 10;
        foreach (var template in templates.All.Where(t => !Existing.Contains(t.Id)))
        {
            var id = $"entity.create.{template.Id}";
            builder.Add(id, template.Group is null ? template.Name : $"{template.Group}: {template.Name}", Category,
                () => hierarchy.Create(template.Id, null), () => documents.Active is not null && !hierarchy.IsLive, null, template.Icon, template.Description);
            builder.Menu(MenuPaths.GameObject + "/2D Object" + (template.Group is { } group ? "/" + group : ""), id, "objects", order++);
        }
    }

    private async Task CopyAsync()
    {
        if (documents.Active is { } model)
            await clipboard.CopyAsync(model, selection.Entities.Where(model.Contains));
    }

    private async Task PasteAsync()
    {
        if (documents.Active is not { } model || await clipboard.ReadAsync() is not { } entities)
            return;
        var anchor = selection.Entities.LastOrDefault(model.Contains);
        Guid? parent = anchor == Guid.Empty ? null : model.Get(anchor).Parent;
        var index = anchor == Guid.Empty ? -1 : model.GetSiblingIndex(anchor) + 1;
        var ids = model.InsertEntities(entities, parent, index, entities.Count(e => e.Parent is null) == 1 ? $"Paste {entities[0].Name}" : "Paste entities");
        selection.SelectEntities(ids);
    }
}
