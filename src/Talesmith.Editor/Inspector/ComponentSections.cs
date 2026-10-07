using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Talesmith.Editor.Inspector.Editors;
using Talesmith.Editor.Prefabs;
using Talesmith.Editor.Undo;
using Talesmith.Runtime.Serialization;
using Talesmith.Scripting;
using Talesmith.UI;
using Talesmith.UI.Controls;

namespace Talesmith.Editor.Inspector;

/// <summary>What the inspector's sections share: the data, the editor factory, the registries and the editing services.</summary>
internal sealed class InspectorContext(InspectorData data, PropertyEditorFactory factory, ComponentRegistry? registry, ScriptTypeRegistry? scripts,
    EntityDataService entities, IUndoService undo, Documents.ISceneDocumentService documents, Action<Uri> openUri, Action rebuild,
    Dictionary<string, bool> collapsed)
{
    public Documents.ISceneDocumentService Documents { get; } = documents;

    public void Open(Uri uri) => openUri(uri);

    public InspectorData Data { get; } = data;

    public PropertyEditorFactory Factory { get; } = factory;

    public ComponentRegistry? Registry { get; } = registry;

    public ScriptTypeRegistry? Scripts { get; } = scripts;

    public EntityDataService Entities { get; } = entities;

    public IUndoService Undo { get; } = undo;

    /// <summary>The scene entities inspected, or empty for live data.</summary>
    public IReadOnlyList<Guid> Targets => (Data as DocumentInspectorData)?.Targets ?? [];

    public bool IsLive => Data.IsLive;

    /// <summary>Which sections of components and scripts are collapsed, by type, kept while the inspector rebuilds its sections.</summary>
    public Dictionary<string, bool> Collapsed { get; } = collapsed;

    /// <summary>Rebuilds the inspector's sections, after the components changed.</summary>
    public void Rebuild() => rebuild();
}

/// <summary>Copied component values, for pasting into a component of the same type.</summary>
internal static class ComponentClipboard
{
    public static (string Type, JsonObject Data)? Copied { get; private set; }

    public static void Copy(string type, JsonObject data) => Copied = (type, (JsonObject)data.DeepClone());
}

/// <summary>Builds the property groups of components and scripts.</summary>
internal static class ComponentSections
{
    private const string Enabled = "enabled";

    // The components the guide documents, each in a section named after its display name.
    private static readonly HashSet<string> Documented = new(StringComparer.Ordinal)
    {
        "Transform", "Tags", "Sprite", "SpriteAnimator", "Camera", "TileMapRenderer", "Collider2D", "Rigidbody2D", "CharacterController2D",
        "TileMapCollider2D", "Light2D", "ShadowCaster2D", "Emissive", "ParticleEmitter", "AudioSource", "AudioListener", "ScriptComponent"
    };

    /// <summary>The group of one component: its icon and name, the enable toggle when it has one, its menu and its properties.</summary>
    public static PropertyGroup Component(InspectorContext context, string type, IReadOnlyList<string> order)
    {
        var definition = context.Registry?.Find(type);
        if (definition is null)
            return Missing(context, type);
        var info = definition.Info;
        var properties = definition.Properties;
        var enabled = properties.FirstOrDefault(p => p.Name == Enabled && p.Kind == PropertyKind.Boolean);
        var group = new PropertyGroup
        {
            Header = info.DisplayName,
            Icon = Icons.Find(info.Icon) ?? Icons.Puzzle,
            IsExpanded = !context.Collapsed.GetValueOrDefault(type),
            Content = context.Factory.CreateRows(properties.Where(p => !ReferenceEquals(p, enabled)), p => context.Data.CreateValue(type, p.Name, p))
        };
        if (info.Description is { } description)
            ToolTip.SetTip(group, description);
        group.PropertyChanged += (_, e) =>
        {
            if (e.Property == PropertyGroup.IsExpandedProperty)
                context.Collapsed[type] = !group.IsExpanded;
        };
        if (enabled is not null)
            group.HeaderActions = EnableToggle(context.Data.CreateValue(type, enabled.Name, enabled), group);
        group.Menu = Menu(context, type, definition, order);
        return group;
    }

    /// <summary>One group per script of the script component, titled with the script's class, and a warning group for each missing script.</summary>
    public static IEnumerable<PropertyGroup> Scripts(InspectorContext context, string type)
    {
        var scripts = context.Data.Get(0, type, "scripts") as JsonArray ?? [];
        for (var i = 0; i < scripts.Count; i++)
        {
            var typeName = JsonValues.Text(scripts[i]?["type"]) ?? "";
            var info = context.Scripts?.Find(typeName);
            var path = $"scripts.{i}";
            var index = i;
            PropertyGroup group;
            if (info is null)
            {
                group = new PropertyGroup
                {
                    Header = $"Missing script: {ShortName(typeName)}",
                    Icon = Icons.AlertTriangle,
                    Content = MissingContent($"No compiled script is called {typeName}. Its saved fields are kept until it compiles again.",
                        scripts[i]?["fields"] as JsonObject)
                };
                group.With(PropertyGroup.IconForegroundProperty, "WarningBrush");
            }
            else
            {
                var fields = context.Scripts!.Describe(info.Type);
                group = new PropertyGroup
                {
                    Header = info.DisplayName,
                    Icon = Icons.FileCode,
                    IsExpanded = !context.Collapsed.GetValueOrDefault($"script:{typeName}"),
                    Content = fields.Count == 0
                        ? new TextBlock { Text = "This script has no public fields.", Classes = { "caption", "muted" }, Margin = new Thickness(0, 2, 0, 4) }
                        : context.Factory.CreateRows(fields, p => context.Data.CreateValue(type, $"{path}.fields.{p.Name}", p))
                };
                group.With(PropertyGroup.IconForegroundProperty, "SuccessBrush");
                ToolTip.SetTip(group, info.TypeName);
                group.PropertyChanged += (_, e) =>
                {
                    if (e.Property == PropertyGroup.IsExpandedProperty)
                        context.Collapsed[$"script:{typeName}"] = !group.IsExpanded;
                };
                group.HeaderActions = EnableToggle(context.Data.CreateValue(type, $"{path}.enabled", new PropertyDescriptor(Enabled, "Enabled", PropertyKind.Boolean, typeof(bool))),
                    group, defaultValue: true);
            }

            group.Menu = ScriptMenu(context, type, index, scripts.Count, scripts[i]?["fields"] as JsonObject);
            yield return group;
        }
    }

    private static CheckBox EnableToggle(PropertyValue value, PropertyGroup group, bool defaultValue = false)
    {
        var toggle = new CheckBox { MinHeight = 0, Padding = default, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(toggle, "Enabled");
        var updating = false;
        toggle.IsCheckedChanged += (_, _) =>
        {
            if (!updating && toggle.IsChecked is { } isChecked)
                value.Set(isChecked);
        };
        return PropertyEditors.Watch(toggle, value, () =>
        {
            updating = true;
            var on = JsonValues.Boolean(value.Get()) ?? defaultValue;
            toggle.IsChecked = value.IsMixed ? null : on;
            if (group.Content is Control content)
                content.Opacity = on || value.IsMixed ? 1 : 0.5;
            updating = false;
        });
    }

    private static MenuFlyout Menu(InspectorContext context, string type, IComponentDefinition definition, IReadOnlyList<string> order)
    {
        var items = new List<Control>();
        var data = context.Data;
        items.Add(Item("Reset", Icons.RotateCcw, () => data.Set(type, "", definition.CreateDefault())));
        items.Add(Item("Copy values", Icons.Copy, () =>
        {
            if (data.Get(0, type, "") is JsonObject values)
                ComponentClipboard.Copy(type, values);
        }));
        var paste = Item("Paste values", Icons.Paste, () =>
        {
            if (ComponentClipboard.Copied is { } copied && copied.Type == type)
                data.Set(type, "", copied.Data.DeepClone());
        });
        items.Add(paste);
        if (!context.IsLive)
        {
            if (data.IsModified(type, ""))
                items.Add(Item("Revert to prefab", Icons.RotateCcw, () => data.Revert(type, "")));
            var index = IndexOf(order, type);
            items.Add(new Separator());
            items.Add(Item("Move up", Icons.ArrowUp, () => Move(context, type, -1), enabled: index > 0 && CanMove(context)));
            items.Add(Item("Move down", Icons.ArrowDown, () => Move(context, type, 1), enabled: index >= 0 && index < order.Count - 1 && CanMove(context)));
            items.Add(new Separator());
            items.Add(Item("Remove component", Icons.Trash, () => Remove(context, type), enabled: type != "Transform"));
        }

        items.Add(new Separator());
        items.Add(Item("Open documentation", Icons.Info, () => context.Open(DocumentationOf(definition))));
        var menu = new MenuFlyout { ItemsSource = items };
        menu.Opening += (_, _) => paste.IsEnabled = ComponentClipboard.Copied?.Type == type;
        return menu;
    }

    private static MenuFlyout ScriptMenu(InspectorContext context, string type, int index, int count, JsonObject? fields)
    {
        var data = context.Data;
        var items = new List<Control>
        {
            Item("Copy fields", Icons.Copy, () =>
            {
                if (fields is not null)
                    ComponentClipboard.Copy($"script-fields:{index}", fields);
            }),
            Item("Paste fields", Icons.Paste, () =>
            {
                if (ComponentClipboard.Copied is { } copied && copied.Type.StartsWith("script-fields:", StringComparison.Ordinal))
                    data.Set(type, $"scripts.{index}.fields", copied.Data.DeepClone());
            })
        };
        if (!context.IsLive)
        {
            items.Add(new Separator());
            items.Add(Item("Move up", Icons.ArrowUp, () => ChangeScripts(context, type, list => Swap(list, index, index - 1)), enabled: index > 0));
            items.Add(Item("Move down", Icons.ArrowDown, () => ChangeScripts(context, type, list => Swap(list, index, index + 1)), enabled: index < count - 1));
            items.Add(new Separator());
            items.Add(Item("Remove script", Icons.Trash, () => ChangeScripts(context, type, list => list.RemoveAt(index))));
        }

        return new MenuFlyout { ItemsSource = items };
    }

    private static void ChangeScripts(InspectorContext context, string type, Action<JsonArray> change)
    {
        var list = (context.Data.Get(0, type, "scripts") as JsonArray)?.DeepClone() as JsonArray ?? [];
        change(list);
        if (list.Count == 0 && context.Targets.Count > 0)
        {
            using var transaction = context.Undo.BeginTransaction("Remove scripts");
            foreach (var id in context.Targets)
                context.Entities.RemoveComponent(id, type);
            return;
        }

        context.Data.Set(type, "scripts", list);
        context.Rebuild();
    }

    private static void Swap(JsonArray list, int a, int b)
    {
        if (a < 0 || b < 0 || a >= list.Count || b >= list.Count)
            return;
        var first = list[a]!.DeepClone();
        list[a] = list[b]!.DeepClone();
        list[b] = first;
    }

    private static bool CanMove(InspectorContext context) => context.Targets.Count == 1 && context.Entities.IsStored(context.Targets[0]);

    private static void Move(InspectorContext context, string type, int delta)
    {
        if (!CanMove(context) || context.Entities.GetComponent(context.Targets[0], type) is not { } component)
            return;
        var id = context.Targets[0];
        var components = context.Entities.GetComponents(id).Select(c => c.Type).ToList();
        var index = components.IndexOf(type) + delta;
        if (index < 0 || index >= components.Count || context.Entities.GetMember(id) is { } && !context.Entities.IsStoredComponent(id, type))
            return;
        var copy = component.Clone();
        using var transaction = context.Undo.BeginTransaction($"Move {type} {(delta < 0 ? "up" : "down")}");
        context.Entities.RemoveComponent(id, type);
        if (context.Entities.IsStored(id))
            context.Documents.Active?.AddComponent(id, copy, index);
    }

    /// <summary>The section of the components guide about a component; components from plugins and scripts share one.</summary>
    public static Uri DocumentationOf(IComponentDefinition definition)
    {
        var section = Documented.Contains(definition.TypeName)
            ? string.Join('-', definition.Info.DisplayName.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            : "components-from-plugins-and-scripts";
        return new Uri($"https://talesmith.dev/guide/scenes/components#{section}");
    }

    private static void Remove(InspectorContext context, string type)
    {
        using var transaction = context.Targets.Count > 1 ? context.Undo.BeginTransaction($"Remove {type} from {context.Targets.Count} entities") : null;
        foreach (var id in context.Targets)
            context.Entities.RemoveComponent(id, type);
    }

    private static PropertyGroup Missing(InspectorContext context, string type)
    {
        var group = new PropertyGroup
        {
            Header = $"Unknown component: {ShortName(type)}",
            Icon = Icons.AlertTriangle,
            Content = MissingContent($"No plugin or script defines {type}. Its data is kept and saved unchanged.", context.Data.Get(0, type, "") as JsonObject)
        };
        group.With(PropertyGroup.IconForegroundProperty, "WarningBrush");
        if (!context.IsLive)
        {
            group.Menu = new MenuFlyout { ItemsSource = new Control[] { Item("Remove component", Icons.Trash, () => Remove(context, type)) } };
        }

        return group;
    }

    private static StackPanel MissingContent(string message, JsonObject? data)
    {
        var stack = new StackPanel { Spacing = 6, Margin = new Thickness(0, 2, 0, 6) };
        stack.Children.Add(new TextBlock { Text = message, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap }.With(TextBlock.ForegroundProperty, "WarningBrush"));
        if (data is { Count: > 0 })
        {
            var json = data.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            stack.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6),
                Child = new SelectableTextBlock { Text = json, Classes = { "mono" }, FontSize = 11, TextWrapping = TextWrapping.Wrap }
            }.With(Border.BackgroundProperty, "SurfaceSunkenBrush"));
        }

        return stack;
    }

    private static MenuItem Item(string header, Geometry icon, Action action, bool enabled = true)
    {
        var item = new MenuItem { Header = header, Icon = new SymbolIcon { Data = icon, Size = 14 }, IsEnabled = enabled };
        item.Click += (_, _) => action();
        return item;
    }

    private static int IndexOf(IReadOnlyList<string> order, string type)
    {
        for (var i = 0; i < order.Count; i++)
        {
            if (order[i] == type)
                return i;
        }

        return -1;
    }

    private static string ShortName(string type) => type[(type.LastIndexOf('.') + 1)..];
}
