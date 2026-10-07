using Avalonia;
using Avalonia.Media;
using Talesmith.Imaging;

namespace Talesmith.Avalonia.Presentation;

/// <summary>Shows the frames a game publishes inside a <see cref="GameView"/>, rendering them with one backend.</summary>
public interface IGamePresenter : IDisposable
{
    /// <summary>Called on the UI thread when the view enters the visual tree.</summary>
    void Attach(GameView view);

    /// <summary>Called on the UI thread when the view leaves the visual tree.</summary>
    void Detach(GameView view);

    /// <summary>Called on the game thread after the game published a frame; with a simulation thread, that is not the UI thread.</summary>
    void OnFramePublished(GameView view);

    /// <summary>Adds the newest frame to the view's drawing; called on the UI thread while the view renders.</summary>
    /// <param name="bounds">The view's area in logical pixels.</param>
    /// <param name="pixelSize">The view's size in device pixels.</param>
    void Render(DrawingContext context, Rect bounds, PixelSize pixelSize);

    /// <summary>Renders the next presented frame into an image as well, as straight from the renderer as possible.</summary>
    Task<ImageData> CaptureAsync();
}
