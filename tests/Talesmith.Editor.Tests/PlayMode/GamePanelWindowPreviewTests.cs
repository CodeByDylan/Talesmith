using System.Numerics;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Avalonia.Presentation;
using Talesmith.Editor.Panels;
using Talesmith.Editor.PlayMode;
using Talesmith.Editor.Shell;
using Talesmith.Input;
using Talesmith.UI.Docking;

namespace Talesmith.Editor.Tests.PlayMode;

public sealed class GamePanelWindowPreviewTests
{
    [Fact]
    public void APreviewedWindowRunsTheGameAtItsSizeInsideThePanelAndTakesThePointer() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var preview = editor.Get<GameWindowPreview>();
        preview.Size = new GameWindowSize(1920, 1080);
        preview.IsEnabled = true;
        var play = editor.Get<IPlayModeService>();
        var window = editor.Get<EditorWindow>();
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            await play.PlayAsync();
            var content = window.GetVisualDescendants().OfType<DockHost>().Single().GetContent(PanelIds.Game)!;
            var game = play.Session!.Game;
            await RefreshUntilAsync(() => game.Viewport.Size == new Vector2(1920, 1080), () => $"the viewport is {game.Viewport.Size}");
            Assert.Equal(1, game.Viewport.DisplayScale);

            var view = content.GetVisualDescendants().OfType<GameView>().Single();
            var frame = content.GetVisualDescendants().OfType<GameWindowFrame>().Single();
            var topLeft = view.TranslatePoint(default, content)!.Value;
            var bottomRight = view.TranslatePoint(new Point(view.Bounds.Width, view.Bounds.Height), content)!.Value;
            Assert.InRange(frame.ActualZoom, 0.05, 0.99);
            Assert.True(topLeft.X >= 0 && topLeft.Y >= 0, $"The window starts at {topLeft}.");
            Assert.True(bottomRight.X <= content.Bounds.Width && bottomRight.Y <= content.Bounds.Height, $"The window ends at {bottomRight} in {content.Bounds.Size}.");
            Assert.Equal(1920 * frame.ActualZoom, bottomRight.X - topLeft.X, 0.01);
            Assert.Equal(GameWindowZoom.Percent(frame.ActualZoom), preview.FittedZoomText);

            var input = game.Services.GetRequiredService<IInputService>();
            var centered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            game.FramePublished += () =>
            {
                if (Vector2.Distance(input.MousePosition, new Vector2(960, 540)) < 2)
                    centered.TrySetResult();
            };
            var center = view.TranslatePoint(new Point(view.Bounds.Width / 2, view.Bounds.Height / 2), window)!.Value;
            await RefreshUntilAsync(() => window.InputHitTest(center) == view, () => $"{window.InputHitTest(center)} is under the window's center");
            view.Focus();
            window.MouseMove(center, RawInputModifiers.None);
            await RefreshUntilAsync(() => centered.Task.IsCompleted, () => $"the game has the pointer at {input.MousePosition}");

            preview.IsEnabled = false;
            window.UpdateLayout();
            await RefreshUntilAsync(() => game.Viewport.Size == new Vector2((float)Math.Ceiling(view.Bounds.Width), (float)Math.Ceiling(view.Bounds.Height)),
                () => $"the viewport is {game.Viewport.Size} for a view of {view.Bounds.Size}");
            Assert.Equal(1, frame.ActualZoom);
        }
        finally
        {
            await play.StopAsync();
            window.CloseApproved = true;
            window.Close();
        }
    });

    [Fact]
    public void TheGameRunsAtThePreviewedDisplayScale() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var preview = editor.Get<GameWindowPreview>();
        preview.Size = new GameWindowSize(3840, 2160, 2);
        preview.Zoom = GameWindowZoom.All.Single(z => z.Factor == 0.25);
        preview.IsEnabled = true;
        var play = editor.Get<IPlayModeService>();
        var window = editor.Get<EditorWindow>();
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            await play.PlayAsync();
            var content = window.GetVisualDescendants().OfType<DockHost>().Single().GetContent(PanelIds.Game)!;
            var game = play.Session!.Game;
            await RefreshUntilAsync(() => game.Viewport.Size == new Vector2(3840, 2160), () => $"the viewport is {game.Viewport.Size}");

            Assert.Equal(2, game.Viewport.DisplayScale);
            var view = content.GetVisualDescendants().OfType<GameView>().Single();
            Assert.Equal(new Size(1920, 1080), view.Bounds.Size);
            Assert.Equal(960, view.TranslatePoint(new Point(1920, 0), content)!.Value.X - view.TranslatePoint(default, content)!.Value.X, 0.01);
        }
        finally
        {
            await play.StopAsync();
            window.CloseApproved = true;
            window.Close();
        }
    });

    /// <summary>Lets the compositor run the play session's frames until <paramref name="condition"/> holds.</summary>
    /// <param name="state">Describes what holds instead, for the failure.</param>
    private static async Task RefreshUntilAsync(Func<bool> condition, Func<string> state)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(30))
                throw new TimeoutException($"The play session did not get there in 30 seconds: {state()}.");
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(16);
        }
    }
}
