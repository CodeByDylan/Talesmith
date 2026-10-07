using Avalonia.Controls;
using Talesmith.Avalonia.Presentation;

namespace Talesmith.Editor.Viewport;

public partial class SceneViewportView : UserControl
{
    private ViewportOverlay? _overlay;

    public SceneViewportView()
    {
        InitializeComponent();
        Chrome.Rail = RailHost;
        Chrome.Zoom = ZoomBar;
    }

    internal void SetOverlay(ViewportOverlay overlay)
    {
        _overlay = overlay;
        Stage.Children.Add(overlay);
    }

    /// <summary>Adds a game view below the ones already shown, which cover it until they are removed.</summary>
    internal void SetGameView(GameView view)
    {
        Stage.Children.Insert(0, view);
        _overlay?.Focus();
    }

    internal void RemoveGameView(GameView view) => Stage.Children.Remove(view);
}
