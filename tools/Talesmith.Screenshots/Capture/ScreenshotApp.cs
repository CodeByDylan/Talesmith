using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Talesmith.UI.Theming;

namespace Talesmith.Screenshots.Capture;

/// <summary>A minimal application that applies the toolkit theme and the editor's styles for headless rendering.</summary>
internal sealed class ScreenshotApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new ToolkitTheme());
        Styles.Add(new StyleInclude(new Uri("avares://Talesmith.Screenshots/")) { Source = new Uri("avares://Talesmith.Editor/Themes/EditorStyles.axaml") });
    }
}
