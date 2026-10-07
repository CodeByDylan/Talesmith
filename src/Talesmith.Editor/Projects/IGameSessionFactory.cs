using Microsoft.Extensions.Logging;
using Talesmith.Avalonia.Hosting;
using Talesmith.Avalonia.Presentation;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Editor.Projects;

/// <summary>Creates game sessions; the default one asks the window's compositor which renderer gets frames to it fastest.</summary>
public interface IGameSessionFactory
{
    Task<GameSession> CreateAsync(DesktopGameOptions options, ILoggerFactory loggers, CancellationToken cancellationToken);
}

/// <summary>Creates sessions shown in Avalonia windows, building the game on the thread pool so the UI stays responsive.</summary>
public sealed class WindowGameSessionFactory : IGameSessionFactory
{
    public Task<GameSession> CreateAsync(DesktopGameOptions options, ILoggerFactory loggers, CancellationToken cancellationToken) =>
        GameSession.CreateForWindowAsync(options, loggers, cancellationToken);
}

/// <summary>Creates sessions without a window, rendering with Skia on the CPU, as tests and screenshots do.</summary>
public sealed class HeadlessGameSessionFactory : IGameSessionFactory
{
    public Task<GameSession> CreateAsync(DesktopGameOptions options, ILoggerFactory loggers, CancellationToken cancellationToken) =>
        Task.FromResult(GameSession.Create(options with { Renderer = RendererPreference.Skia, Audio = false }, loggers, WindowGraphics.None));
}
