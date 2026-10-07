using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Avalonia.Overlays;

namespace Talesmith.Samples.HexQuest;

/// <summary>A small card in the top-right corner with the map title, the hero's position and the controls.</summary>
public sealed class HudOverlay : IGameOverlay
{
    public Control Create(IServiceProvider services)
    {
        var hud = services.GetRequiredService<QuestHud>();
        var title = new TextBlock { FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White };
        var location = new TextBlock { FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)), Margin = new Thickness(0, 2, 0, 8) };
        var help = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            Text = "WASD or arrows to walk · click to travel\nWheel to zoom · E to talk · Esc menu"
        };

        void Refresh(QuestHudState state)
        {
            title.Text = state.MapTitle;
            location.Text = string.Create(CultureInfo.InvariantCulture, $"{state.Terrain} · hex ({state.Cell.X}, {state.Cell.Y})");
        }

        hud.View.Changed += Refresh;
        Refresh(hud.View.Value);
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(205, 15, 23, 42)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 12),
            Margin = new Thickness(16),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Child = new StackPanel { Children = { title, location, help } }
        };
    }
}
