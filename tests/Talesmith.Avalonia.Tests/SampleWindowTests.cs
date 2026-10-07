using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using Talesmith.Avalonia.Hosting;
using Talesmith.Avalonia.Presentation;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Avalonia.Tests;

/// <summary>Plays the sample games in headless windows as the player does: on a simulation thread, with their HUDs and pause menu.</summary>
public sealed class SampleWindowTests
{
    [Fact]
    public void HexQuestShowsItsHudAndPausesFromTheMenuWhileRunningOnItsSimulationThread() => HeadlessApp.Run(async () =>
    {
        var options = new DesktopGameOptions { AssetRoot = SampleAssets("HexQuest"), Renderer = RendererPreference.Skia, Audio = false, DeveloperTools = false };
        var session = GameSession.Create(options, NullLoggerFactory.Instance, WindowGraphics.None);
        var game = session.Game;
        var window = new GameWindow(session, captureDirectory: null);
        window.Show();
        try
        {
            Assert.Equal(GameThreading.Dedicated, game.Threading);

            await RefreshUntilAsync(() => Text(window, text => text.Contains(" · hex (", StringComparison.Ordinal) && !text.StartsWith(" · ", StringComparison.Ordinal)));
            await RefreshUntilAsync(() => !window.LoadingScreen.IsVisible);
            window.Host!.View.Focus();
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            await RefreshUntilAsync(() => window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Paused" && t.IsEffectivelyVisible));
            Assert.True(await game.InvokeAsync(() => game.IsPaused).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        }
        finally
        {
            await CloseAsync(window, game);
        }
    });

    [Fact]
    public void ThePauseMenuKeepsFocusWhenTheBarsBesideTheViewAreClicked() => HeadlessApp.Run(async () =>
    {
        var options = new DesktopGameOptions { AssetRoot = SampleAssets("IsleHopper"), Renderer = RendererPreference.Skia, Audio = false, DeveloperTools = false };
        var session = GameSession.Create(options, NullLoggerFactory.Instance, WindowGraphics.None);
        var window = new GameWindow(session, captureDirectory: null) { Width = 1600, Height = 720 };
        window.Show();
        try
        {
            var view = window.Host!.View;
            await RefreshUntilAsync(() => Text(window, text => text.Contains("emeralds", StringComparison.Ordinal)));
            await RefreshUntilAsync(() => session.Game.Viewport.Layout.ViewRect.X == 160);
            await RefreshUntilAsync(() => !window.LoadingScreen.IsVisible);
            view.Focus();
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            await RefreshUntilAsync(() => window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Paused" && t.IsEffectivelyVisible));
            await RefreshUntilAsync(() => !view.IsFocused);

            window.MouseDown(new Point(40, 360), MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(new Point(40, 360), MouseButton.Left, RawInputModifiers.None);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            Assert.False(view.IsFocused);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Paused" && t.IsEffectivelyVisible);
        }
        finally
        {
            await CloseAsync(window, session.Game);
        }
    });

    private static bool Text(Window window, Func<string, bool> match) =>
        window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text is { } text && match(text));

    /// <summary>Closes the window and waits until its game's simulation thread has ended, so no frame of it runs during another test.</summary>
    private static async Task CloseAsync(GameWindow window, Game game)
    {
        var simulation = game.Simulation;
        window.Close();
        if (simulation is not null)
            await RefreshUntilAsync(() => !simulation.IsRunning);
    }

    private static async Task RefreshUntilAsync(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(30))
                throw new TimeoutException("The game did not reach the expected state.");
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(16, TestContext.Current.CancellationToken);
        }
    }

    private static string SampleAssets(string game)
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "Talesmith.slnx")))
                return Path.Combine(folder.FullName, "samples", game, "assets");
        }

        throw new DirectoryNotFoundException("The repository root was not found.");
    }
}
