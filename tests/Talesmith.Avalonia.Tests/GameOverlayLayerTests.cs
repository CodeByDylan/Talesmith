using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Avalonia.Hosting;
using Talesmith.Avalonia.Overlays;
using Talesmith.Avalonia.Presentation;
using Talesmith.Imaging;
using Talesmith.Rendering;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Avalonia.Tests;

/// <summary>Overlays lay out in overlay units on the game's view rectangle and take pointer input at any scale.</summary>
public sealed class GameOverlayLayerTests
{
    private static readonly ViewSettings Design = new() { Width = 320, Height = 180 };

    [Fact]
    public void OverlaysArePlacedOnTheViewAndScaledToIt() => HeadlessApp.Run(async () =>
    {
        await using var game = CreateGame(Design);
        var (window, host, button) = Show(game, 800, 600);

        Assert.Equal(new Size(320, 180), host.Overlays.OverlaySize);
        Assert.Equal(2.5, host.Overlays.OverlayScale);
        Assert.Equal(new Size(60, 20), button.Bounds.Size);
        Assert.Equal(new Point(650, 475), button.TranslatePoint(default, window));
        Assert.Equal(new Point(800, 525), button.TranslatePoint(new Point(60, 20), window));
        window.Close();
    });

    [Fact]
    public void ButtonsAreClickableAtScale() => HeadlessApp.Run(async () =>
    {
        await using var game = CreateGame(Design);
        var (window, _, button) = Show(game, 800, 600);
        var clicks = 0;
        button.Click += (_, _) => clicks++;

        Click(window, new Point(560, 100));
        Click(window, new Point(790, 515));

        Assert.Equal(1, clicks);
        window.Close();
    });

    [Fact]
    public void LayoutFollowsTheHostSize() => HeadlessApp.Run(async () =>
    {
        await using var game = CreateGame(Design with { ScaleMode = ViewScaleMode.Expand });
        var (window, host, button) = Show(game, 1400, 800);
        window.Content = null;
        var frame = new Border { Width = 640, Height = 480, Child = host };
        window.Content = frame;
        window.UpdateLayout();

        Assert.Equal(new Size(320, 240), host.Overlays.OverlaySize);
        Assert.Equal(2, host.Overlays.OverlayScale);
        Assert.Equal(new Point(520, 440), button.TranslatePoint(default, host));

        frame.Width = 1280;
        frame.Height = 720;
        window.UpdateLayout();

        Assert.Equal(new Size(320, 180), host.Overlays.OverlaySize);
        Assert.Equal(4, host.Overlays.OverlayScale);
        Assert.Equal(new Point(1040, 640), button.TranslatePoint(default, host));
        window.Close();
    });

    [Fact]
    public void GamesWithoutAViewLayOutOverlaysInLogicalPixels() => HeadlessApp.Run(async () =>
    {
        await using var game = CreateGame(ViewSettings.Unscaled);
        var (window, host, button) = Show(game, 800, 600);

        Assert.Equal(new Size(800, 600), host.Overlays.OverlaySize);
        Assert.Equal(1, host.Overlays.OverlayScale);
        Assert.Equal(new Point(740, 580), button.TranslatePoint(default, window));
        window.Close();
    });

    [Fact]
    public void AFitViewLaysOverlaysOutAtTheOverlaySize() => HeadlessApp.Run(async () =>
    {
        await using var game = CreateGame(Design with { OverlayWidth = 640, OverlayHeight = 360 });
        var (window, host, button) = Show(game, 800, 600);

        Assert.Equal(new Size(640, 360), host.Overlays.OverlaySize);
        Assert.Equal(1.25, host.Overlays.OverlayScale);
        Assert.Equal(new Size(60, 20), button.Bounds.Size);
        Assert.Equal(new Point(725, 500), button.TranslatePoint(default, window));
        Assert.Equal(new Point(800, 525), button.TranslatePoint(new Point(60, 20), window));
        window.Close();
    });

    [Fact]
    public void AnOverlaySizeOfAnotherShapeKeepsTheViewShape() => HeadlessApp.Run(async () =>
    {
        await using var game = CreateGame(Design with { OverlayWidth = 640, OverlayHeight = 400 });
        var (window, host, _) = Show(game, 800, 600);

        Assert.Equal(new Size(640, 360), host.Overlays.OverlaySize);
        Assert.Equal(1.25, host.Overlays.OverlayScale);
        window.Close();
    });

    [Fact]
    public void ExpandAndCropScaleTheOverlaySizeLikeTheView() => HeadlessApp.Run(async () =>
    {
        await using var expand = CreateGame(Design with { ScaleMode = ViewScaleMode.Expand, OverlayWidth = 640, OverlayHeight = 360 });
        var (expandWindow, expandHost, expandButton) = Show(expand, 640, 480);

        Assert.Equal(new Size(640, 480), expandHost.Overlays.OverlaySize);
        Assert.Equal(1, expandHost.Overlays.OverlayScale);
        Assert.Equal(new Point(580, 460), expandButton.TranslatePoint(default, expandWindow));
        expandWindow.Close();

        await using var crop = CreateGame(Design with { ScaleMode = ViewScaleMode.Crop, OverlayWidth = 640, OverlayHeight = 360 });
        var (cropWindow, cropHost, cropButton) = Show(crop, 960, 720);

        Assert.Equal(new Size(480, 360), cropHost.Overlays.OverlaySize);
        Assert.Equal(2, cropHost.Overlays.OverlayScale);
        Assert.Equal(new Point(840, 680), cropButton.TranslatePoint(default, cropWindow));
        cropWindow.Close();
    });

    [Fact]
    public void UnscaledViewsIgnoreTheOverlaySize() => HeadlessApp.Run(async () =>
    {
        await using var game = CreateGame(ViewSettings.Unscaled with { OverlayWidth = 1280, OverlayHeight = 720 });
        var (window, host, _) = Show(game, 800, 600);

        Assert.Equal(new Size(800, 600), host.Overlays.OverlaySize);
        Assert.Equal(1, host.Overlays.OverlayScale);
        window.Close();
    });

    [Fact]
    public void ButtonsAreClickableAtTheOverlayScale() => HeadlessApp.Run(async () =>
    {
        await using var game = CreateGame(Design with { OverlayWidth = 640, OverlayHeight = 360 });
        var (window, _, button) = Show(game, 800, 600);
        var clicks = 0;
        button.Click += (_, _) => clicks++;

        Click(window, new Point(715, 515));
        Click(window, new Point(790, 545));
        Click(window, new Point(730, 505));
        Click(window, new Point(795, 520));

        Assert.Equal(2, clicks);
        window.Close();
    });

    [Fact]
    public void OverlaysFollowTheDisplayScaleSetOnAnAncestor() => HeadlessApp.Run(async () =>
    {
        await using var game = CreateGame(Design with { IntegerScale = true });
        var (window, host, button) = Show(game, 800, 600);

        Assert.Equal(2, host.Overlays.OverlayScale);
        Assert.Equal(new Point(600, 440), button.TranslatePoint(default, window));

        // 1600 × 1200 pixels take the 320 × 180 design five times, leaving bars above and below.
        GameView.SetDisplayScale(host, 2);
        window.UpdateLayout();

        Assert.Equal(new Size(320, 180), host.Overlays.OverlaySize);
        Assert.Equal(2.5, host.Overlays.OverlayScale);
        Assert.Equal(new Point(650, 475), button.TranslatePoint(default, window));
        window.Close();
    });

    private static Game CreateGame(ViewSettings view)
    {
        var builder = GameBuilder.Create(AppContext.BaseDirectory, new GameSettings { View = view });
        builder.Services.AddSingleton<IGameUi, AvaloniaGameUi>();
        builder.Services.AddSingleton<IGameOverlay, CornerButtonOverlay>();
        return builder.Build();
    }

    private static (Window Window, GameHost Host, Button Button) Show(Game game, double width, double height)
    {
        var host = new GameHost(game, new NullPresenter(), captureDirectory: null);
        var window = new Window { Width = width, Height = height, Content = host };
        window.Show();
        window.UpdateLayout();
        return (window, host, (Button)host.Overlays.Overlays.OfType<Panel>().Single().Children.Single());
    }

    private static void Click(Window window, Point point)
    {
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
    }

    /// <summary>A 60×20 button in the bottom-right corner of the view.</summary>
    private sealed class CornerButtonOverlay : IGameOverlay
    {
        public Control Create(IServiceProvider services) => new Panel
        {
            Children =
            {
                new Button
                {
                    Width = 60,
                    Height = 20,
                    Padding = default,
                    MinHeight = 0,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Background = Brushes.Orange
                }
            }
        };
    }

    private sealed class NullPresenter : IGamePresenter
    {
        public void Attach(GameView view)
        {
        }

        public void Detach(GameView view)
        {
        }

        public void OnFramePublished(GameView view)
        {
        }

        public void Render(DrawingContext context, Rect bounds, PixelSize pixelSize)
        {
        }

        public Task<ImageData> CaptureAsync() => Task.FromException<ImageData>(new NotSupportedException());

        public void Dispose()
        {
        }
    }
}
