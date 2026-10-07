using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Ecs;
using Talesmith.Input;
using Talesmith.Mathematics;
using Talesmith.Rendering;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Hosting;
using Talesmith.Runtime.Rendering;
using Talesmith.Runtime.Scenes;

namespace Talesmith.Runtime.Tests.Hosting;

/// <summary>A game with a 640×360 fit view: frames, cameras and conversions follow the layout at every window size.</summary>
public sealed class ViewScalingTests : IAsyncDisposable
{
    private static readonly ViewSettings Design = new() { Width = 640, Height = 360, BorderColor = new Color(9, 8, 7) };
    private static readonly Rect2 CameraBounds = new(0, 0, 2000, 1000);

    private readonly Game _game;

    public ViewScalingTests()
    {
        var builder = GameBuilder.Create(AppContext.BaseDirectory, new GameSettings { View = Design, StartScene = new SceneRequest(CameraScene.Name) });
        builder.Services.AddScene<CameraScene>(CameraScene.Name);
        _game = builder.Build();
        _game.Viewport.Resize(new Vector2(1280, 800), 2);
        _game.Start();
        for (var i = 0; i < 600 && (_game.Scenes.Current is null || _game.Scenes.IsLoading); i++)
        {
            _game.Tick(1.0 / 60);
            Thread.Sleep(1);
        }
    }

    public ValueTask DisposeAsync() => _game.DisposeAsync();

    [Fact]
    public void ViewportLayoutFollowsTheSettings()
    {
        Assert.Same(Design, _game.Viewport.View);
        Assert.Equal(ViewLayout.Compute(new Vector2(1280, 800), 2, Design), _game.Viewport.Layout);
        Assert.Equal(new Rect2(0, 40, 1280, 720), _game.Viewport.Layout.ViewRect);
        Assert.Equal(ViewLayout.Whole(new Vector2(1280, 800), 2), _game.Viewport.UnscaledLayout);
    }

    [Fact]
    public void FramesDrawTheSceneCameraScaledIntoTheViewRect()
    {
        var frame = Tick();

        Assert.Equal(_game.Viewport.Layout, frame.View);
        Assert.Equal(Design.BorderColor, frame.BorderColor);
        Assert.Equal(2 * 1.5f, frame.Camera.Zoom);
        Assert.Equal(640 / 1.5f, frame.VisibleBounds.Width, 2);
        Assert.Equal(360 / 1.5f, frame.VisibleBounds.Height, 2);
    }

    [Fact]
    public void LargerWindowsShowTheSameWorldArea()
    {
        var small = Tick().VisibleBounds;
        _game.Viewport.Resize(new Vector2(3840, 2400), 1);
        var large = Tick().VisibleBounds;

        Assert.Equal(small.Width, large.Width, 2);
        Assert.Equal(small.Height, large.Height, 2);
    }

    [Fact]
    public void CameraBoundsClampTheDesignArea()
    {
        var world = _game.Scenes.Current!.World;
        ref var camera = ref world.Get<Camera>(Single<Camera>(world));
        camera.View.Position = new Vector2(5000, -5000);
        Tick();

        var position = world.Get<Camera>(Single<Camera>(world)).View.Position;
        Assert.Equal(CameraBounds.Right - 640 / 1.5f / 2, position.X, 2);
        Assert.Equal(CameraBounds.Top + 360 / 1.5f / 2, position.Y, 2);
    }

    [Fact]
    public void ScreenConversionsGoThroughTheViewRect()
    {
        var frame = Tick();
        var render = _game.Services.GetRequiredService<RenderContext>();
        var viewTopLeft = frame.View.ViewRect.Position;

        Assert.Equal(frame.VisibleBounds.Position, render.ScreenToWorld(viewTopLeft));
        Assert.Equal(Vector2.Zero, render.ScreenToView(viewTopLeft));
        Assert.Equal(new Vector2(320, 180), render.ScreenToView(new Vector2(640, 400)));
        Assert.True(render.ScreenToView(new Vector2(640, 10)).Y < 0, "The top bar lies above the view");
        var world = new Vector2(812, 431);
        Assert.Equal(world.X, render.ScreenToWorld(render.WorldToScreen(world)).X, 2);
        Assert.Equal(world.Y, render.ScreenToWorld(render.WorldToScreen(world)).Y, 2);
    }

    [Fact]
    public void MouseOverTheViewMapsToViewUnits()
    {
        _game.Services.GetRequiredService<IInputSink>().MouseMove(new Vector2(1280, 760));
        Tick();
        var render = _game.Services.GetRequiredService<RenderContext>();

        Assert.Equal(new Vector2(640, 360), render.ScreenToView(_game.Services.GetRequiredService<IInputService>().MousePosition));
    }

    [Fact]
    public void CameraOverrideKeepsTheWholeTargetInLogicalPixels()
    {
        _game.CameraOverride = new Camera2D(new Vector2(100, 100), 1);
        var frame = Tick();

        Assert.Equal(_game.Viewport.UnscaledLayout, frame.View);
        Assert.Equal(2, frame.Camera.Zoom);
        Assert.Equal(new Vector2(640, 400), frame.View.ViewSize);
    }

    private RenderFrame Tick()
    {
        _game.Tick(1.0 / 60);
        var frame = _game.Frames.BeginRead()!;
        _game.Frames.EndRead(frame);
        return frame;
    }

    private static Entity Single<T>(World world)
    {
        world.Query<T>().TryGetSingle(out var entity);
        return entity;
    }

    private sealed class CameraScene : Scene
    {
        public const string Name = "view-scaling";

        protected override Task LoadAsync(CancellationToken cancellationToken)
        {
            World.Create(new Camera(new Vector2(1000, 500), 1.5f) { Bounds = CameraBounds });
            return Task.CompletedTask;
        }
    }
}
