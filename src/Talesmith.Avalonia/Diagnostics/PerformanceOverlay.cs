using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Avalonia.Diagnostics;

/// <summary>A heads-up display of frame rate, frame times, the slowest markers and counters, refreshed four times per second.</summary>
public sealed class PerformanceOverlay : Border
{
    private static readonly FontFamily Mono = new("Cascadia Mono, JetBrains Mono, DejaVu Sans Mono, Consolas, monospace");

    private readonly Game _game;
    private readonly TextBlock _summary = CreateText(12);
    private readonly TextBlock _details = CreateText(11);
    private readonly TextBlock _status = CreateText(11);
    private readonly FrameGraph _graph = new() { Height = 56, Margin = new Thickness(0, 6) };
    private readonly DispatcherTimer _timer;
    private DateTime _statusUntil;

    public PerformanceOverlay(Game game)
    {
        _game = game;
        _graph.Profiler = game.Profilers.Game;
        Background = new SolidColorBrush(Color.FromArgb(200, 12, 13, 18));
        BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(12, 10);
        Margin = new Thickness(12);
        Width = 430;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        IsHitTestVisible = false;
        _status.Foreground = new SolidColorBrush(Color.FromRgb(129, 140, 248));
        Child = new StackPanel { Children = { _summary, _graph, _details, _status } };
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Refresh();
    }

    /// <summary>Shows a short message, such as where a report was saved, for a few seconds.</summary>
    public void ShowStatus(string message)
    {
        _status.Text = message;
        _statusUntil = DateTime.UtcNow.AddSeconds(4);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void Refresh()
    {
        if (!IsVisible)
            return;

        var invariant = CultureInfo.InvariantCulture;
        var game = _game.Profilers.Game.GetStatistics(120);
        var render = _game.Profilers.Render.GetStatistics(120);
        var info = _game.Renderer.Info;
        _summary.Text = string.Create(invariant,
            $"{game.AverageFramesPerSecond,5:0} fps  {game.FrameInterval.Average,6:0.00} ms  p99 {game.FrameInterval.P99,6:0.00} ms\n" +
            $"{info.Backend} · {info.Device}\n" +
            $"game {game.FrameWork.Average,6:0.00} ms  render {render.FrameWork.Average,6:0.00} ms  alloc {game.AllocatedBytes.Average,7:0} B");

        var details = new StringBuilder();
        foreach (var marker in game.Markers.Concat(render.Markers).OrderByDescending(m => m.Milliseconds.Average).Take(12))
            details.AppendLine(invariant, $"{Shorten(marker.Name),-30}{marker.Milliseconds.Average,7:0.000}  p95 {marker.Milliseconds.P95,7:0.000}");
        details.AppendLine();
        foreach (var counter in game.Counters.Concat(render.Counters))
            details.AppendLine(invariant, $"{Shorten(counter.Name),-30}{counter.Last,9:#,0.##}");
        _details.Text = details.ToString().TrimEnd();
        if (DateTime.UtcNow > _statusUntil)
            _status.Text = "F3 hide · F4 debug views · F9 save report · F12 screenshot";
        _graph.InvalidateVisual();
    }

    private static string Shorten(string name) => name.Length <= 29 ? name : string.Concat(name.AsSpan(0, 28), "…");

    private static TextBlock CreateText(double size) => new()
    {
        FontFamily = Mono,
        FontSize = size,
        Foreground = Brushes.White,
        TextWrapping = TextWrapping.NoWrap
    };
}
