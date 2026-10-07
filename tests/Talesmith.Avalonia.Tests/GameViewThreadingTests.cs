using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Avalonia.Overlays;
using Talesmith.Avalonia.Presentation;
using Talesmith.Imaging;
using Talesmith.Runtime.Hosting;
using Key = Talesmith.Input.Key;

namespace Talesmith.Avalonia.Tests;

/// <summary>A game view showing a game that runs on a simulation thread, with the compositor driven by hand.</summary>
[Collection(nameof(FrameTiming))]
public sealed class GameViewThreadingTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void TheViewDrivesTheSimulationThreadWithoutTickingTheGame() => HeadlessApp.Run(async () =>
    {
        await using var game = CreateGame();
        var presenter = new RecordingPresenter();
        new SimulationThread(game) { RefreshRate = 60 }.Start();
        var window = new Window { Width = 320, Height = 200, Content = new GameView(game, presenter) };
        window.Show();

        await RefreshUntilAsync(() => presenter.Published > 5);

        Assert.Equal(GameThreading.Dedicated, game.Threading);
        Assert.Equal(new Vector2(320, 200), game.Viewport.Size);
        Assert.Equal(0, presenter.PublishedOnUiThread);
        window.Close();
    });

    [Fact]
    public void ABlockedUiThreadDoesNotStopTheGame() => HeadlessApp.Run(async () =>
    {
        await using var game = CreateGame();
        var presenter = new RecordingPresenter();
        var simulation = new SimulationThread(game) { RefreshRate = 60 };
        simulation.Start();
        var window = new Window { Width = 320, Height = 200, Content = new GameView(game, presenter) };
        window.Show();
        await RefreshUntilAsync(() => presenter.Published > 0);

        // Blocks the UI thread, so the view reports no refreshes, until the game has published five more frames.
        var before = presenter.Published;
        var blocked = System.Diagnostics.Stopwatch.StartNew();
        while (presenter.Published - before < 5 && blocked.Elapsed < Timeout)
            Thread.Sleep(10);
        var published = presenter.Published - before;
        var seconds = blocked.Elapsed.TotalSeconds;

        Assert.True(published >= 5, $"The game published {published} frames while the UI thread was blocked for {seconds:0.0} s.");
        // The view sets the rate of the monitor the window would be on, when it can read it.
        Assert.True(published <= simulation.RefreshRate * seconds + 2, $"{published} frames in {seconds:0.000} s is faster than {simulation.RefreshRate:0} Hz.");
        window.Close();
    });

    [Fact]
    public void KeysPressedInTheViewReachTheGameOnItsThread() => HeadlessApp.Run(async () =>
    {
        await using var game = CreateGame();
        var input = game.Services.GetRequiredService<Talesmith.Input.IInputService>();
        var pressed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        game.FramePublished += () =>
        {
            if (input.IsDown(Key.Space))
                pressed.TrySetResult(game.IsGameThread);
        };
        new SimulationThread(game) { RefreshRate = 60 }.Start();
        var view = new GameView(game, new RecordingPresenter());
        var window = new Window { Width = 320, Height = 200, Content = view };
        window.Show();
        view.Focus();
        await RefreshAsync(TimeSpan.FromMilliseconds(50));

        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);

        Assert.True(await pressed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        window.Close();
    });

    [Fact]
    public void AnAncestorsDisplayScaleSetsThePixelsTheGameRendersAndTheMouseMovesIn() => HeadlessApp.Run(async () =>
    {
        await using var game = CreateGame();
        var input = game.Services.GetRequiredService<Talesmith.Input.IInputService>();
        var moved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        game.FramePublished += () =>
        {
            if (input.MousePosition == new Vector2(80, 50))
                moved.TrySetResult();
        };
        new SimulationThread(game) { RefreshRate = 60 }.Start();
        var view = new GameView(game, new RecordingPresenter());
        var host = new Border { Width = 160, Height = 100, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = view };
        GameView.SetDisplayScale(host, 2);
        var window = new Window { Width = 320, Height = 200, Content = host };
        window.Show();
        view.Focus();

        await RefreshUntilAsync(() => game.Viewport.Size == new Vector2(320, 200));
        Assert.Equal(2, game.Viewport.DisplayScale);

        window.MouseMove(new Point(40, 25), RawInputModifiers.None);

        await moved.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        window.Close();
    });

    [Fact]
    public void ViewStatePublishedOnTheSimulationThreadReachesTheUiThread() => HeadlessApp.Run(async () =>
    {
        await using var game = CreateGame();
        var state = new ViewState<FrameText>(game.Services.GetRequiredService<IGameUi>(), new FrameText(""));
        var label = new TextBlock();
        var changedOnUiThread = true;
        state.Changed += text =>
        {
            changedOnUiThread &= Dispatcher.UIThread.CheckAccess();
            label.Text = text.Text;
        };
        game.FramePublished += () => state.Publish(new FrameText($"Frame {game.FrameCount}"));
        new SimulationThread(game) { RefreshRate = 60 }.Start();

        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (label.Text is null && clock.Elapsed < TimeSpan.FromSeconds(10))
            await RefreshAsync(TimeSpan.FromMilliseconds(50));

        Assert.True(changedOnUiThread);
        Assert.StartsWith("Frame ", label.Text);
    });

    private static Game CreateGame()
    {
        var builder = GameBuilder.Create(AppContext.BaseDirectory, new GameSettings());
        builder.Services.AddSingleton<IGameUi, AvaloniaGameUi>();
        return builder.Build();
    }

    /// <summary>Runs the compositor at about 60 frames per second, letting the UI thread work, until <paramref name="condition"/> holds.</summary>
    private static async Task RefreshUntilAsync(Func<bool> condition)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < Timeout, "The game view did not reach the expected state.");
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(16, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Runs the compositor at about 60 frames per second while letting the UI thread work.</summary>
    private static async Task RefreshAsync(TimeSpan duration)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed < duration)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(16, TestContext.Current.CancellationToken);
        }
    }

    private sealed record FrameText(string Text);

    private sealed class RecordingPresenter : IGamePresenter
    {
        private int _published;
        private int _publishedOnUiThread;

        public int Published => Volatile.Read(ref _published);

        public int PublishedOnUiThread => Volatile.Read(ref _publishedOnUiThread);

        public void Attach(GameView view)
        {
        }

        public void Detach(GameView view)
        {
        }

        public void OnFramePublished(GameView view)
        {
            Interlocked.Increment(ref _published);
            if (Dispatcher.UIThread.CheckAccess())
                Interlocked.Increment(ref _publishedOnUiThread);
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
