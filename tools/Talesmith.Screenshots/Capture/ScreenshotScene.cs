using Avalonia;
using Avalonia.Controls;

namespace Talesmith.Screenshots.Capture;

/// <summary>One screenshot: the content to render, its logical size and optional steps before capturing.</summary>
/// <remarks>Add a scene by deriving from this class and listing it in <see cref="SceneCatalog"/>.</remarks>
internal abstract class ScreenshotScene
{
    /// <summary>Gets the file name without theme suffix or extension, such as <c>toolkit-gallery</c>.</summary>
    public abstract string Name { get; }

    /// <summary>Gets the logical size of the window.</summary>
    public virtual Size Size => new(1280, 800);

    /// <summary>Creates the window content. Called once per theme, after the theme is applied.</summary>
    public abstract Control Build();

    /// <summary>Runs after the theme is applied and before capturing, for example to open a flyout or start a drag.</summary>
    public virtual void Prepare(Window window)
    {
    }

    /// <summary>Gets the control to crop to, or null to capture the whole window.</summary>
    public virtual Control? Region(Window window) => null;

    /// <summary>Runs after each capture, once the window is closed, to free what <see cref="Build"/> created, such as an open editor.</summary>
    /// <remarks>One process renders every screenshot, so whatever a scene keeps alive adds up across the run.</remarks>
    public virtual void Release()
    {
    }
}
