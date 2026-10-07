using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Avalonia.Presentation;
using Talesmith.Rendering;
using Talesmith.Rendering.Skia;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Editor.Tests.Viewport;

public sealed class SkiaPresenterTests
{
    [Fact]
    public void RedrawingWhatLiesAboveTheViewDoesNotRenderTheSameFrameAgain() => Headless.Run(async () =>
    {
        await using var game = GameBuilder.Create(AppContext.BaseDirectory, new GameSettings()).Build();
        var pacing = game.Services.GetRequiredService<FramePacing>();
        pacing.VSync = false;
        pacing.MaxFramesPerSecond = 1;
        using var renderer = new SkiaRenderer();
        using var presenter = new SkiaPresenter(renderer, game);
        var overlay = new Border { Background = Brushes.Transparent };
        var window = new Window { Width = 320, Height = 200, Content = new Panel { Children = { new GameView(game, presenter), overlay } } };
        var rendered = 0;
        game.Profilers.Render.FrameCompleted += _ => rendered++;
        window.Show();
        for (var i = 0; i < 200 && rendered == 0; i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
            Pump();
        }

        Assert.True(rendered > 0);
        var before = rendered;
        for (var i = 0; i < 5; i++)
        {
            overlay.InvalidateVisual();
            Pump();
        }

        Assert.InRange(rendered - before, 0, 1);
        window.Close();
    });

    [Theory]
    [InlineData(BitmapInterpolationMode.None, true)]
    [InlineData(BitmapInterpolationMode.Unspecified, false)]
    public void AMagnifiedFrameIsSampledByTheInterpolationModeSetAroundTheView(BitmapInterpolationMode mode, bool sharp) => Headless.Run(async () =>
    {
        var settings = new GameSettings { View = new ViewSettings { Width = 4, Height = 4, BorderColor = Mathematics.Color.White } };
        await using var game = GameBuilder.Create(AppContext.BaseDirectory, settings).Build();
        using var renderer = new SkiaRenderer();
        using var presenter = new SkiaPresenter(renderer, game);
        var magnifier = new Border
        {
            Child = new GameView(game, presenter) { Width = 8, Height = 4 },
            RenderTransform = new ScaleTransform(8, 8),
            RenderTransformOrigin = RelativePoint.TopLeft,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        RenderOptions.SetBitmapInterpolationMode(magnifier, mode);
        var window = new Window { Width = 100, Height = 50, Content = magnifier };
        var rendered = 0;
        game.Profilers.Render.FrameCompleted += _ => rendered++;
        window.Show();
        for (var i = 0; i < 200 && rendered == 0; i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
            Pump();
        }

        Assert.True(rendered > 0);
        using var shot = window.CaptureRenderedFrame()!;
        // White bars 2 pixels wide frame the 4 by 4 view, so the row crosses an edge at 16.
        var row = Enumerable.Range(8, 16).Select(x => Pixel(shot, x, 16)).ToList();
        Assert.NotEqual(row[0], row[^1]);
        Assert.Equal(sharp, row.All(pixel => pixel == row[0] || pixel == row[^1]));
        window.Close();
    });

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static uint Pixel(WriteableBitmap bitmap, int x, int y)
    {
        using var buffer = bitmap.Lock();
        return (uint)Marshal.ReadInt32(buffer.Address, y * buffer.RowBytes + x * 4);
    }
}
