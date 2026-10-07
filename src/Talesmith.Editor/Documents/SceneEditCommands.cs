using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Editor.Commands;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Selection;
using Talesmith.Editor.Viewport;
using Talesmith.Runtime.Serialization;
using Talesmith.UI;

namespace Talesmith.Editor.Documents;

/// <summary>The commands that edit the open scene: creating, duplicating and deleting entities, selecting, and adding components.</summary>
public sealed class SceneEditCommands(ISceneDocumentService documents, ISelectionService selection, IProjectService project, ViewportCamera camera,
    Undo.IUndoService undo)
    : IEditorCommandContributor
{
    private readonly MenuItemViewModel _addComponent = new() { Header = "Add", Icon = Icons.Plus };
    private readonly List<RelayCommand> _componentCommands = [];

    void IEditorCommandContributor.Contribute(CommandBuilder builder)
    {
        const string Edit = "Edit";
        const string Entity = "GameObject";
        bool HasScene() => documents.Active is not null;
        bool HasSelection() => documents.Active is not null && selection.Entities.Count > 0;

        builder.Add("edit.delete", "Delete", Edit, Delete, HasSelection, "Delete", Icons.Trash, "Deletes the selected entities and their children.");
        builder.Add("edit.duplicate", "Duplicate", Edit, Duplicate, HasSelection, "Ctrl+D", Icons.CopyPlus, "Copies the selected entities next to the originals.");
        builder.Add("edit.selectAll", "Select all", Edit, () => selection.SelectEntities(documents.Active?.Entities.Select(e => e.Id) ?? []), HasScene, "Ctrl+A", Icons.SquareDashed);
        builder.Add("edit.deselect", "Deselect", Edit, selection.Clear, () => !selection.IsEmpty, "Escape", Icons.X);
        builder.KeepOutOfText("edit.delete", "edit.duplicate", "edit.selectAll");
        builder.Menu(MenuPaths.Edit, "edit.duplicate", "entities");
        builder.Menu(MenuPaths.Edit, "edit.delete", "entities");
        builder.Menu(MenuPaths.Edit, "edit.selectAll", "selection");
        builder.Menu(MenuPaths.Edit, "edit.deselect", "selection");

        builder.Add("entity.createEmpty", "Create empty", Entity, () => Create("Entity", parent: false, []), HasScene, "Ctrl+Shift+N", Icons.Box,
            "Creates an entity at the center of the view.");
        builder.Add("entity.createChild", "Create empty child", Entity, () => Create("Entity", parent: true, []), HasSelection, "Alt+Shift+N", Icons.ListTree,
            "Creates an entity under the selected one.");
        builder.Add("entity.createCamera", "Camera", Entity, () => Create("Camera", parent: false, [new ComponentDocument("Camera", new JsonObject { ["zoom"] = 1 })]),
            HasScene, null, Icons.Camera);
        builder.Add("entity.createSprite", "Sprite", Entity, () => Create("Sprite", parent: false, [new ComponentDocument("Sprite", [])]), HasScene, null, Icons.Image);
        builder.Menu(MenuPaths.GameObject, "entity.createEmpty", "create");
        builder.Menu(MenuPaths.GameObject, "entity.createChild", "create");
        builder.Menu(MenuPaths.GameObject + "/2D Object", "entity.createSprite", "objects");
        builder.Menu(MenuPaths.GameObject + "/2D Object", "entity.createCamera", "objects");
        builder.Menu(MenuPaths.GameObject, "edit.duplicate", "edit");
        builder.Menu(MenuPaths.GameObject, "edit.delete", "edit");

        builder.Menu(MenuPaths.Component, _addComponent, "add");
        project.StatusChanged += (_, _) => FillComponentMenu();
        project.EditSessionChanged += (_, _) =>
        {
            _addComponent.Items.Clear();
            _componentCommands.Clear();
            FillComponentMenu();
        };
        selection.Changed += (_, _) =>
        {
            foreach (var command in _componentCommands)
                command.NotifyCanExecuteChanged();
        };
        FillComponentMenu();
    }

    private void Create(string name, bool parent, IEnumerable<ComponentDocument> extra)
    {
        if (documents.Active is not { } model)
            return;
        Guid? parentId = parent && selection.Entities.Count > 0 ? selection.Entities[^1] : null;
        var position = parentId is null ? camera.Position : System.Numerics.Vector2.Zero;
        var components = new List<ComponentDocument> { new("Transform", new JsonObject { ["position"] = JsonFormats.WriteVector2(Round(position)) }) };
        components.AddRange(extra);
        var entity = model.CreateEntity(UniqueName(model, name, parentId), parentId, components);
        selection.SelectEntity(entity.Id);
    }

    private static System.Numerics.Vector2 Round(System.Numerics.Vector2 value) => new(MathF.Round(value.X), MathF.Round(value.Y));

    private static string UniqueName(SceneDocumentModel model, string name, Guid? parent)
    {
        var names = model.GetChildren(parent).Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        if (!names.Contains(name))
            return name;
        for (var i = 1; ; i++)
        {
            if (!names.Contains($"{name} {i}"))
                return $"{name} {i}";
        }
    }

    private void Delete()
    {
        if (documents.Active is not { } model)
            return;
        var ids = selection.Entities.ToList();
        selection.SelectEntities([]);
        model.DeleteEntities(ids);
    }

    private void Duplicate()
    {
        if (documents.Active is not { } model)
            return;
        var copies = model.DuplicateEntities(selection.Entities);
        selection.SelectEntities(copies);
    }

    private void FillComponentMenu()
    {
        if (_addComponent.Items.Count > 0 || project.EditSession?.Game.Services.GetService<ComponentRegistry>() is not { } registry)
            return;
        foreach (var category in registry.Definitions.Where(d => !d.Info.Hidden).GroupBy(d => d.Info.Category).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var group = new MenuItemViewModel { Header = category.Key, Items = new ObservableCollection<MenuItemViewModel>() };
            foreach (var definition in category.OrderBy(d => d.Info.DisplayName, StringComparer.Ordinal))
            {
                var command = new RelayCommand(() => AddComponent(definition), () => documents.Active is not null && selection.Entities.Count > 0);
                _componentCommands.Add(command);
                group.Items.Add(new MenuItemViewModel
                {
                    Header = definition.Info.DisplayName,
                    Command = command,
                    Icon = Icons.Find(definition.Info.Icon) ?? Icons.Puzzle,
                    ToolTip = definition.Info.Description
                });
            }

            _addComponent.Items.Add(group);
        }
    }

    private void AddComponent(IComponentDefinition definition)
    {
        if (documents.Active is not { } model)
            return;
        using var transaction = undo.BeginTransaction($"Add {definition.Info.DisplayName}");
        foreach (var id in selection.Entities.Where(model.Contains))
        {
            if (model.Get(id).FindComponent(definition.TypeName) is null)
                model.AddComponent(id, new ComponentDocument(definition.TypeName, definition.CreateDefault()));
        }
    }
}
