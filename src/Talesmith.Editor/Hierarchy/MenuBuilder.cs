using Avalonia.Controls;
using Talesmith.Editor.Commands;
using Talesmith.UI.Controls;

namespace Talesmith.Editor.Hierarchy;

/// <summary>Turns menu item view models into the menu items of context menus and flyouts.</summary>
public static class MenuBuilder
{
    public static IReadOnlyList<Control> Build(IEnumerable<MenuItemViewModel> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var result = new List<Control>();
        foreach (var item in items)
        {
            if (item.IsSeparator)
            {
                if (result.Count > 0 && result[^1] is not Separator)
                    result.Add(new Separator());
                continue;
            }

            var menuItem = new MenuItem
            {
                Header = item.Header,
                Command = item.Command,
                CommandParameter = item.CommandParameter,
                InputGesture = item.Gesture,
                Icon = item.Icon is null ? null : new SymbolIcon { Data = item.Icon, Size = 14 }
            };
            if (item.ToolTip is { } tip)
                ToolTip.SetTip(menuItem, tip);
            if (item.Items.Count > 0)
                menuItem.ItemsSource = Build(item.Items);
            result.Add(menuItem);
        }

        while (result.Count > 0 && result[^1] is Separator)
            result.RemoveAt(result.Count - 1);
        return result;
    }
}
