using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Inspector.Editors;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Undo;
using Talesmith.Runtime.Serialization;
using Talesmith.Scripting;
using Talesmith.UI;
using Talesmith.UI.Controls;

namespace Talesmith.Editor.Inspector;

/// <summary>The Add Component button and its searchable popup: components by category with icons and descriptions, recently used ones first,
/// and the project's scripts. Enter adds the top hit.</summary>
internal sealed class AddComponentMenu
{
    private const string RecentKey = "inspector.recentComponents";
    private const int MaxRecent = 6;
    private const string ScriptsCategory = "Scripts";

    private readonly ISceneDocumentService _documents;
    private readonly IUndoService _undo;
    private readonly ProjectState _state;
    private readonly Func<ComponentRegistry?> _registry;
    private readonly Func<ScriptTypeRegistry?> _scripts;

    public AddComponentMenu(ISceneDocumentService documents, IUndoService undo, ProjectState state, Func<ComponentRegistry?> registry, Func<ScriptTypeRegistry?> scripts)
    {
        _documents = documents;
        _undo = undo;
        _state = state;
        _registry = registry;
        _scripts = scripts;
    }

    /// <summary>The button that opens the popup for the given entities.</summary>
    public Button CreateButton(Func<IReadOnlyList<Guid>> targets)
    {
        var button = new Button
        {
            Classes = { "small" },
            Margin = new Thickness(12, 12, 12, 16),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { new SymbolIcon { Data = Icons.Plus, Size = 14 }, new TextBlock { Text = "Add component" } }
            }
        };
        ToolTip.SetTip(button, "Add a component or script to the selected entities");
        var flyout = PickerList.Attach(button, query => Items(query, targets()), item => Add(item, targets()), "Search components and scripts", 320);
        flyout.Placement = PlacementMode.BottomEdgeAlignedLeft;
        button.Flyout = flyout;
        return button;
    }

    /// <summary>The choices for a search: recent ones and every category when empty, the best matches first otherwise.</summary>
    public IEnumerable<PickerItem> Items(string query, IReadOnlyList<Guid> targets)
    {
        var model = _documents.Active;
        bool Has(string type) => model is not null && targets.Count > 0 && targets.All(id => model.Find(id)?.FindComponent(type) is not null);
        var components = (_registry()?.Definitions ?? []).Where(d => !d.Info.Hidden && d.ComponentType != typeof(ScriptComponent) && !Has(d.TypeName)).ToList();
        var scripts = _scripts()?.Types ?? [];

        if (query.Length > 0)
        {
            var hits = new List<(int Rank, PickerItem Item)>();
            foreach (var definition in components)
            {
                if (Rank(query, definition.Info.DisplayName, definition.Info.Category, definition.Info.Description) is { } rank)
                    hits.Add((rank, Component(definition, showCategory: true)));
            }

            foreach (var script in scripts)
            {
                if (Rank(query, script.DisplayName, ScriptsCategory, script.TypeName) is { } rank)
                    hits.Add((rank, Script(script)));
            }

            foreach (var (_, item) in hits.OrderBy(h => h.Rank).ThenBy(h => h.Item.Title, StringComparer.OrdinalIgnoreCase))
                yield return item;
            yield break;
        }

        var recent = Recent().Select(name => components.FirstOrDefault(d => d.TypeName == name)).OfType<IComponentDefinition>().ToList();
        if (recent.Count > 0)
        {
            yield return new PickerItem("Recently used") { IsHeading = true };
            foreach (var definition in recent)
                yield return Component(definition, showCategory: false);
        }

        foreach (var category in components.GroupBy(d => d.Info.Category).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            yield return new PickerItem(category.Key) { IsHeading = true };
            foreach (var definition in category.OrderBy(d => d.Info.DisplayName, StringComparer.Ordinal))
                yield return Component(definition, showCategory: false);
        }

        if (scripts.Count > 0)
        {
            yield return new PickerItem(ScriptsCategory) { IsHeading = true };
            foreach (var script in scripts)
                yield return Script(script);
        }
    }

    /// <summary>Adds the chosen component or script to every entity that does not have it, as one undo step.</summary>
    public void Add(PickerItem item, IReadOnlyList<Guid> targets)
    {
        if (_documents.Active is not { } model)
            return;
        var ids = targets.Where(model.Contains).ToList();
        if (ids.Count == 0)
            return;
        switch (item.Value)
        {
            case IComponentDefinition definition:
            {
                using var transaction = _undo.BeginTransaction($"Add {definition.Info.DisplayName}");
                foreach (var id in ids)
                {
                    if (model.Get(id).FindComponent(definition.TypeName) is null)
                        model.AddComponent(id, new ComponentDocument(definition.TypeName, definition.CreateDefault()));
                }

                Remember(definition.TypeName);
                break;
            }
            case ScriptTypeInfo script:
                AddScript(model, ids, script);
                break;
        }
    }

    private void AddScript(SceneDocumentModel model, IReadOnlyList<Guid> ids, ScriptTypeInfo script)
    {
        var type = _registry()?.Find(typeof(ScriptComponent))?.TypeName ?? ComponentRegistry.GetTypeName(typeof(ScriptComponent));
        using var transaction = _undo.BeginTransaction($"Add script {script.DisplayName}");
        foreach (var id in ids)
        {
            var entry = new JsonObject { ["type"] = script.TypeName, ["enabled"] = true, ["fields"] = new JsonObject() };
            if (model.Get(id).FindComponent(type) is { } existing)
            {
                var scripts = (existing.Data["scripts"] as JsonArray)?.DeepClone() as JsonArray ?? [];
                scripts.Add(entry);
                model.SetProperty(id, type, "scripts", scripts);
            }
            else
            {
                model.AddComponent(id, new ComponentDocument(type, new JsonObject { ["scripts"] = new JsonArray(entry) }));
            }
        }
    }

    private static PickerItem Component(IComponentDefinition definition, bool showCategory) =>
        new(definition.Info.DisplayName, Icons.Find(definition.Info.Icon) ?? Icons.Puzzle,
            showCategory ? $"{definition.Info.Category} · {definition.Info.Description}" : definition.Info.Description, definition)
        {
            IconBrush = "AccentBrush"
        };

    private static PickerItem Script(ScriptTypeInfo script) => new(script.DisplayName, Icons.FileCode, script.TypeName, script) { IconBrush = "SuccessBrush" };

    /// <summary>How well a query matches: lower is better; null when it does not match.</summary>
    private static int? Rank(string query, string name, string category, string? description)
    {
        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return 0;
        if (name.Split(' ').Any(w => w.StartsWith(query, StringComparison.OrdinalIgnoreCase)))
            return 1;
        if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
            return 2;
        if (category.Contains(query, StringComparison.OrdinalIgnoreCase))
            return 3;
        if (description?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
            return 4;
        return null;
    }

    private List<string> Recent() => _state.Get<List<string>>(RecentKey) ?? [];

    private void Remember(string type)
    {
        var recent = Recent();
        recent.Remove(type);
        recent.Insert(0, type);
        if (recent.Count > MaxRecent)
            recent.RemoveRange(MaxRecent, recent.Count - MaxRecent);
        _state.Set(RecentKey, recent);
    }
}
