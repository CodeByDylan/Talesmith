using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Talesmith.Assets;
using Talesmith.Avalonia.Presentation;
using Talesmith.Editor.Projects;
using Talesmith.Rendering.Skia;
using Talesmith.Runtime.Rendering;
using Talesmith.VFX;
using Talesmith.VFX.Rendering;

namespace Talesmith.Editor.Particles.Preview;

/// <summary>The Skia renderer and particle textures shared by every particle preview, independent of the scene's renderer.</summary>
/// <remarks>Frames are built on the UI thread and drawn by Avalonia's render thread through <see cref="Renderer"/>; project textures load
/// through the edit game's asset manager and are uploaded to this renderer.</remarks>
public sealed class ParticlePreviewRenderer : IDisposable
{
    private readonly IProjectService _project;
    private readonly ILoggerFactory _loggers;
    private ParticleAssets? _assets;
    private TextureCache? _textures;
    private IServiceProvider? _game;

    public ParticlePreviewRenderer(IProjectService project, ILoggerFactory? loggers = null)
    {
        _project = project;
        _loggers = loggers ?? NullLoggerFactory.Instance;
        Renderer = new SkiaRenderer();
    }

    public SkiaRenderer Renderer { get; }

    public bool IsDisposed { get; private set; }

    /// <summary>The textures particles are drawn with: built-in ones at once, project ones once loaded.</summary>
    public ParticleAssets Assets
    {
        get
        {
            var game = _project.EditSession?.Game.Services;
            if (_assets is not null && ReferenceEquals(game, _game))
                return _assets;
            _assets?.Dispose();
            _textures?.Dispose();
            _game = game;
            _textures = game is null ? null : new TextureCache(Renderer, _project.Settings);
            _assets = new ParticleAssets(Renderer, new PreviewServices(game, _textures), _loggers.CreateLogger<ParticleAssets>());
            return _assets;
        }
    }

    public void Dispose()
    {
        lock (SkiaRenderThread.Gate)
        {
            if (IsDisposed)
                return;
            IsDisposed = true;
        }

        _assets?.Dispose();
        _textures?.Dispose();
        SkiaRenderThread.Release(Renderer);
    }

    private sealed class PreviewServices(IServiceProvider? game, TextureCache? textures) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(TextureCache) ? textures
            : serviceType == typeof(IAssetManager) || serviceType == typeof(IAssetCatalog) ? game?.GetService(serviceType)
            : null;
    }
}

/// <summary>Writes an emitter's particles into a frame with the preview renderer's textures.</summary>
internal static class ParticleFrameWriter
{
    public static int Draw(Rendering.RenderFrame frame, ParticleAssets assets, ParticleSimulation simulation, ParticleSettings settings, ref Rendering.SpriteInstance[] buffer)
    {
        var count = simulation.AliveCount;
        if (count == 0 || !settings.Renderer.Enabled)
            return 0;
        if (buffer.Length < count)
            buffer = new Rendering.SpriteInstance[Math.Max(count, buffer.Length * 2)];
        var region = assets.GetTexture(settings.Renderer);
        var written = simulation.WriteInstances(buffer, region.Source, System.Numerics.Matrix3x2.Identity);
        if (written > 0)
            frame.Draw(region.Texture, ParticleAssets.MaterialFor(settings.Renderer.Blend), buffer.AsSpan(0, written), settings.Renderer.Layer);
        return written;
    }
}
