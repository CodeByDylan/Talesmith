using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Avalonia.Overlays;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Samples.Shared;

/// <summary>A small frame-rate counter in the bottom-left corner, shown when the player turns it on in the settings.</summary>
public sealed class FrameRateOverlay : IGameOverlay
{
    private const int SampleFrames = 60;

    public int Order => 50;

    public Control Create(IServiceProvider services)
    {
        var menu = services.GetRequiredService<GameMenu>();
        var profiler = services.GetRequiredService<Game>().Profilers.Game;
        var text = new TextBlock
        {
            FontFamily = new FontFamily("Cascadia Mono, JetBrains Mono, DejaVu Sans Mono, Consolas, monospace"),
            FontSize = 13,
            Foreground = Brushes.White
        };
        var badge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(190, 15, 23, 42)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 5),
            Margin = new Thickness(16),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false,
            Child = text
        };

        void Update()
        {
            var statistics = profiler.GetStatistics(SampleFrames);
            text.Text = string.Create(CultureInfo.InvariantCulture,
                $"{statistics.AverageFramesPerSecond:0} fps  {statistics.FrameInterval.Average:0.0} ms");
        }

        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => Update();

        void Refresh(object? sender, PropertyChangedEventArgs? e)
        {
            badge.IsVisible = menu.ShowFrameRate;
            if (badge.IsVisible)
            {
                Update();
                timer.Start();
            }
            else
            {
                timer.Stop();
            }
        }

        menu.PropertyChanged += Refresh;
        Refresh(null, null);
        return badge;
    }
}
