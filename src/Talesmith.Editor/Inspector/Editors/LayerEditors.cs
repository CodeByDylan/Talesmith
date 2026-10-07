using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Authoring;
using Talesmith.Editor.Projects;
using Talesmith.Physics;
using Talesmith.Runtime.Serialization;
using Talesmith.UI.Controls;

namespace Talesmith.Editor.Inspector.Editors;

/// <summary>The names of the 32 layers of each <see cref="LayerSet"/>: collision layers from the project's physics settings.</summary>
public sealed class LayerNames(IProjectService project)
{
    private const int Count = 32;
    private (string Path, DateTime Written, PhysicsConfiguration Configuration)? _physics;

    /// <summary>The name of every layer, "Default" and "Layer N" where the project names none.</summary>
    public IReadOnlyList<string> Get(LayerSet set)
    {
        var names = new string[Count];
        var physics = set == LayerSet.Physics ? Physics() : null;
        for (var i = 0; i < Count; i++)
            names[i] = physics?.GetLayerName(i) ?? (i == 0 ? "Default" : string.Create(CultureInfo.InvariantCulture, $"Layer {i}"));
        return names;
    }

    /// <summary>The layers the project gave a name of its own.</summary>
    public IReadOnlySet<int> Named(LayerSet set) =>
        set == LayerSet.Physics && Physics() is { } physics
            ? physics.LayerNames.Where(p => !string.IsNullOrWhiteSpace(p.Value)).Select(p => p.Key).ToHashSet()
            : new HashSet<int>();

    private PhysicsConfiguration? Physics()
    {
        string path;
        try
        {
            path = Path.Combine(project.Project.AssetRoot, PhysicsConfiguration.FileName);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        var written = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        if (_physics is { } cached && cached.Path == path && cached.Written == written)
            return cached.Configuration;
        PhysicsConfiguration configuration;
        try
        {
            configuration = written == DateTime.MinValue ? new PhysicsConfiguration() : PhysicsConfiguration.Load(project.Project.AssetRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or System.Text.Json.JsonException)
        {
            configuration = new PhysicsConfiguration();
        }

        _physics = (path, written, configuration);
        return configuration;
    }
}

/// <summary>Layers and layer masks: a drop-down of layer names, or a drop-down of checkboxes with Everything and Nothing.</summary>
public sealed class LayerEditorProvider : IPropertyEditorProvider
{
    public int Priority => 1;

    public Control? CreateEditor(PropertyEditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var property = context.Property;
        if (property.Layers is not { } set || property.Kind != PropertyKind.Integer || context.Services.GetService<LayerNames>() is not { } names)
            return null;
        return property.IsLayerMask ? Mask(property, context.Value, names, set) : Single(context.Value, names, set);
    }

    /// <summary>The mask's bits as saved: an unsigned number for unsigned fields, a signed one otherwise.</summary>
    internal static JsonNode ToJson(uint mask, Type valueType) =>
        valueType == typeof(uint) || valueType == typeof(uint?) ? JsonValue.Create(mask) : JsonValue.Create(unchecked((int)mask));

    internal static uint ToMask(JsonNode? node)
    {
        if (node is not JsonValue value)
            return uint.MaxValue;
        if (value.TryGetValue<long>(out var number))
            return unchecked((uint)number);
        if (value.TryGetValue<double>(out var real))
            return unchecked((uint)(long)real);
        return uint.MaxValue;
    }

    /// <summary>How a mask reads in the closed drop-down.</summary>
    internal static string Summary(uint mask, IReadOnlyList<string> names)
    {
        if (mask == uint.MaxValue)
            return "Everything";
        if (mask == 0)
            return "Nothing";
        var set = Enumerable.Range(0, names.Count).Where(i => (mask & (1u << i)) != 0).ToList();
        return set.Count switch
        {
            1 => names[set[0]],
            2 => $"{names[set[0]]}, {names[set[1]]}",
            _ when set.Count == names.Count - 1 => "Everything but " + names[Enumerable.Range(0, names.Count).First(i => (mask & (1u << i)) == 0)],
            _ => string.Create(CultureInfo.CurrentCulture, $"{set.Count} layers")
        };
    }

    private static ComboBox Single(IPropertyValue value, LayerNames names, LayerSet set)
    {
        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 26, FontSize = 12, MaxDropDownHeight = 320 };
        var updating = false;
        void Fill()
        {
            updating = true;
            combo.ItemsSource = names.Get(set).Select((n, i) => string.Create(CultureInfo.InvariantCulture, $"{i}  {n}")).ToList();
            updating = false;
        }

        Fill();
        combo.DropDownOpened += (_, _) =>
        {
            var selected = combo.SelectedIndex;
            Fill();
            updating = true;
            combo.SelectedIndex = selected;
            updating = false;
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (!updating && combo.SelectedIndex >= 0)
                value.Set(combo.SelectedIndex);
        };
        return PropertyEditors.Watch(combo, value, () =>
        {
            updating = true;
            var layer = JsonValues.Number(value.Get()) is { } number ? (int)number : 0;
            combo.SelectedIndex = value.IsMixed || layer is < 0 or > 31 ? -1 : layer;
            combo.PlaceholderText = value.IsMixed ? "—" : null;
            updating = false;
        });
    }

    private static Button Mask(PropertyDescriptor property, IPropertyValue value, LayerNames names, LayerSet set)
    {
        var text = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            MinHeight = 26,
            Padding = new Thickness(8, 0, 6, 0),
            Content = new DockPanel
            {
                Children =
                {
                    new SymbolIcon { Data = UI.Icons.ChevronDown, Size = 12, [DockPanel.DockProperty] = Dock.Right }.With(SymbolIcon.ForegroundProperty, "TextMutedBrush"),
                    text
                }
            }
        };

        var boxes = new CheckBox[32];
        var list = new StackPanel { Spacing = 1 };
        var updating = false;
        for (var i = 0; i < boxes.Length; i++)
        {
            var bit = 1u << i;
            var box = new CheckBox { FontSize = 12, MinHeight = 24 };
            box.IsCheckedChanged += (_, _) =>
            {
                if (updating || box.IsChecked is not { } on)
                    return;
                value.Update(node => ToJson(on ? ToMask(node) | bit : ToMask(node) & ~bit, property.ValueType));
            };
            boxes[i] = box;
            list.Children.Add(box);
        }

        Button Action(string label, uint mask)
        {
            var action = new Button { Content = label, Classes = { "small" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
            action.Click += (_, _) => value.Set(ToJson(mask, property.ValueType));
            return action;
        }

        var actions = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 6), Children = { Action("Everything", uint.MaxValue), Action("Nothing", 0) } };
        actions.Children[0].Margin = new Thickness(0, 0, 3, 0);
        actions.Children[1].Margin = new Thickness(3, 0, 0, 0);
        var content = new DockPanel { MinWidth = 200, Children = { actions, new ScrollViewer { MaxHeight = 300, Content = list } } };
        DockPanel.SetDock(actions, Dock.Top);

        void Refresh()
        {
            updating = true;
            var labels = names.Get(set);
            var named = names.Named(set);
            var mask = ToMask(value.Get());
            for (var i = 0; i < boxes.Length; i++)
            {
                boxes[i].Content = string.Create(CultureInfo.InvariantCulture, $"{i}  {labels[i]}");
                boxes[i].Opacity = i == 0 || named.Contains(i) || set != LayerSet.Physics ? 1 : 0.6;
                boxes[i].IsChecked = (mask & (1u << i)) != 0;
            }

            text.Text = value.IsMixed ? "—" : Summary(mask, labels);
            ToolTip.SetTip(button, value.IsMixed ? null : string.Join(", ", Enumerable.Range(0, 32).Where(i => (mask & (1u << i)) != 0).Select(i => labels[i])));
            updating = false;
        }

        var flyout = new Flyout { Content = content, Placement = global::Avalonia.Controls.PlacementMode.BottomEdgeAlignedLeft };
        flyout.Opening += (_, _) => Refresh();
        button.Flyout = flyout;
        return PropertyEditors.Watch(button, value, Refresh);
    }
}
