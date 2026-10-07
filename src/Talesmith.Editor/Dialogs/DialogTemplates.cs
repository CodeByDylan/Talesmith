using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml;

namespace Talesmith.Editor.Dialogs;

/// <summary>Loads the data templates that map dialog view models to their views.</summary>
public static class DialogTemplates
{
    public static IEnumerable<IDataTemplate> Load() =>
        (global::Avalonia.Controls.Templates.DataTemplates)AvaloniaXamlLoader.Load(new Uri("avares://Talesmith.Editor/Dialogs/DialogTemplates.axaml"));
}
