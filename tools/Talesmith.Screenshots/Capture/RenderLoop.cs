using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;

namespace Talesmith.Screenshots.Capture;

/// <summary>Drives the headless dispatcher and renderer.</summary>
internal static class RenderLoop
{
    /// <summary>Shows a window at the given logical size, rendered at <paramref name="scale"/> pixels per unit as on a high-DPI display.</summary>
    public static void Host(Window window, double width, double height, double scale)
    {
        window.Width = width;
        window.Height = height;
        window.Show();
        window.SetRenderScaling(scale);
        Pump(6);
    }

    public static void Pump(int frames = 3)
    {
        for (var i = 0; i < frames; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Renders frames at a realistic cadence so animations, transitions and timers settle.</summary>
    public static void Settle(int milliseconds = 400)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < milliseconds)
        {
            Pump(1);
            Thread.Sleep(16);
        }
    }

    /// <summary>Pumps until <paramref name="condition"/> holds.</summary>
    /// <exception cref="TimeoutException">The condition did not hold within the timeout.</exception>
    public static void Wait(Func<bool> condition, int timeoutMilliseconds = 10000)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.ElapsedMilliseconds < timeoutMilliseconds)
        {
            Pump(1);
            Thread.Sleep(10);
        }

        if (!condition())
            throw new TimeoutException("A screenshot scene did not reach its expected state.");
    }
}
