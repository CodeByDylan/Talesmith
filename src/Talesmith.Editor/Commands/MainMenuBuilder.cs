using System.Collections.ObjectModel;

namespace Talesmith.Editor.Commands;

/// <summary>Builds the main menu from the registry's menu entries: top-level menus in <see cref="MenuPaths.TopLevel"/> order, submenus by path
/// and groups separated by lines.</summary>
public static class MainMenuBuilder
{
    public static ObservableCollection<MenuItemViewModel> Build(EditorCommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var root = new Node("");
        foreach (var top in MenuPaths.TopLevel)
            root.Child(top);
        foreach (var (entry, sequence) in registry.MenuEntries.Select((e, i) => (e, i)))
        {
            var node = root;
            foreach (var part in entry.Path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                node = node.Child(part, entry.Group, entry.Order, sequence);
            node.Entries.Add((entry, sequence));
        }

        var menu = new ObservableCollection<MenuItemViewModel>();
        foreach (var top in root.Children.Where(c => c.HasContent))
            menu.Add(Create(top, registry));
        return menu;
    }

    private static MenuItemViewModel Create(Node node, EditorCommandRegistry registry)
    {
        var item = new MenuItemViewModel { Header = node.Name };
        var leaves = new List<(string Group, int Order, int Sequence, MenuItemViewModel Item)>();
        foreach (var (entry, sequence) in node.Entries)
        {
            var leaf = entry.Item ?? (entry.CommandId is { } id && registry.Find(id) is { } command ? MenuItemViewModel.For(command) : null);
            if (leaf is not null)
                leaves.Add((entry.Group, entry.Order, sequence, leaf));
        }

        foreach (var child in node.Children.Where(c => c.HasContent))
            leaves.Add((child.Group, child.Order, child.Sequence, Create(child, registry)));

        var groupOrder = leaves.GroupBy(l => l.Group).Select(g => (g.Key, First: g.Min(l => l.Sequence))).OrderBy(g => g.First).Select(g => g.Key).ToList();
        string? previous = null;
        foreach (var leaf in leaves.OrderBy(l => groupOrder.IndexOf(l.Group)).ThenBy(l => l.Order).ThenBy(l => l.Sequence))
        {
            if (previous is not null && previous != leaf.Group)
                item.Items.Add(MenuItemViewModel.Separator);
            item.Items.Add(leaf.Item);
            previous = leaf.Group;
        }

        return item;
    }

    private sealed class Node(string name, string group = "", int order = 0, int sequence = 0)
    {
        public string Name { get; } = name;

        public string Group { get; } = group;

        public int Order { get; } = order;

        public int Sequence { get; } = sequence;

        public List<Node> Children { get; } = [];

        public List<(MenuEntry Entry, int Sequence)> Entries { get; } = [];

        public bool HasContent => Entries.Count > 0 || Children.Any(c => c.HasContent);

        public Node Child(string childName, string childGroup = "", int childOrder = 0, int childSequence = 0)
        {
            var child = Children.Find(c => string.Equals(c.Name, childName, StringComparison.OrdinalIgnoreCase));
            if (child is null)
            {
                child = new Node(childName, childGroup, childOrder, childSequence);
                var insertBefore = Name.Length == 0 && !MenuPaths.TopLevel.Contains(childName) ? Children.FindIndex(c => c.Name == MenuPaths.Window) : -1;
                if (insertBefore >= 0)
                    Children.Insert(insertBefore, child);
                else
                    Children.Add(child);
            }

            return child;
        }
    }
}
