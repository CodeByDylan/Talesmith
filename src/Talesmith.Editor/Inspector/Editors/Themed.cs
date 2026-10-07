using Avalonia;
using Avalonia.Controls;

namespace Talesmith.Editor.Inspector.Editors;

/// <summary>Binds control properties to theme resources from code, so they follow theme switches.</summary>
internal static class Themed
{
    public static T With<T>(this T control, AvaloniaProperty property, string resourceKey)
        where T : Control
    {
        control.Bind(property, control.GetResourceObservable(resourceKey));
        return control;
    }
}
