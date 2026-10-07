using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Talesmith.Ecs;
using Talesmith.Imaging;
using Talesmith.Rendering;
using Talesmith.Runtime.Hosting;
using Talesmith.Runtime.Scenes;
using Talesmith.Systems;

namespace Talesmith.Lighting.Tests;

/// <summary>A headless game with lighting whose only scene is built by a test, rendered offscreen with a real renderer.</summary>
public sealed class LitGame : IAsyncDisposable
{
    public const int Width = 640;
    public const int Height = 360;

    private readonly string _assetRoot = Directory.CreateTempSubdirectory("talesmith-lighting").FullName;

    public LitGame(IRenderer renderer, Action<World, IRenderer> build, ExecutionModes mode = ExecutionModes.Play)
    {
        Renderer = renderer;
        var builder = GameBuilder.Create(_assetRoot, new GameSettings { StartScene = new SceneRequest(TestScene.Name) });
        builder.UseRenderer(renderer);
        builder.Services.AddSingleton(new SceneSetup(build));
        builder.Services.AddScene<TestScene>(TestScene.Name);
        builder.Services.AddTalesmithLighting();
        Game = builder.Build();
        Game.Mode = mode;
        Game.Viewport.Size = new Vector2(Width, Height);
        Game.Start();
        for (var i = 0; i < 600 && (Game.Scenes.Current is null || Game.Scenes.IsLoading); i++)
        {
            Game.Tick(1.0 / 60);
            Thread.Sleep(1);
        }

        // Lets the scene's fade-in finish.
        for (var i = 0; i < 40; i++)
            Game.Tick(1.0 / 60);
    }

    public Game Game { get; }

    public IRenderer Renderer { get; }

    public LightingEnvironment Environment => Game.Scenes.Current!.Services.GetRequiredService<LightingEnvironment>();

    public World World => Game.Scenes.Current!.World;

    /// <summary>Runs one frame and returns it; the frame stays valid until the next call.</summary>
    public RenderFrame Tick()
    {
        Game.Tick(1.0 / 60);
        var frame = Game.Frames.BeginRead()!;
        Game.Frames.EndRead(frame);
        return frame;
    }

    public ImageData Render() => ((IOffscreenRenderer)Renderer).RenderToImage(Tick(), Width, Height);

    public static string Save(ImageData image, string name)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "captures", "lighting");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name + ".png");
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var skImage = SKImage.FromPixelCopy(info, image.Pixels.AsSpan(), image.Stride);
        using var data = skImage.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
        return path;
    }

    public static float Brightness(ImageData image, int x, int y)
    {
        var i = y * image.Stride + x * 4;
        return (image.Pixels[i] + image.Pixels[i + 1] + image.Pixels[i + 2]) / (3f * 255);
    }

    public async ValueTask DisposeAsync()
    {
        await Game.DisposeAsync();
        Renderer.Dispose();
        Directory.Delete(_assetRoot, recursive: true);
    }

    private sealed class TestScene(SceneSetup setup, IRenderer renderer) : Scene
    {
        public const string Name = "lighting-test";

        protected override Task LoadAsync(CancellationToken cancellationToken)
        {
            setup.Build(World, renderer);
            return Task.CompletedTask;
        }
    }

    private sealed record SceneSetup(Action<World, IRenderer> Build);
}
