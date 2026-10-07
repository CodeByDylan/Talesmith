using Talesmith.Rendering;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Avalonia.Presentation;

/// <summary>A renderer together with the presenter that shows its frames in a <see cref="GameView"/>.</summary>
/// <remarks>Dispose the backend after the game and its presenter, since the game's textures belong to the renderer.</remarks>
public interface IRenderBackend : IDisposable
{
    IRenderer Renderer { get; }

    IGamePresenter CreatePresenter(Game game);
}
