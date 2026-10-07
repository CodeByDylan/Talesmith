using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Avalonia.Overlays;
using Talesmith.Samples.Shared;

namespace Talesmith.Samples.IsleHopper;

/// <summary>The HUD: a card with the level, emeralds and time, the text of the sign the hero stands at, and the finish screen.</summary>
public sealed class HudOverlay : IGameOverlay
{
    private static readonly IBrush Card = new SolidColorBrush(Color.FromArgb(205, 15, 23, 42));
    private static readonly IBrush CardBorder = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
    private static readonly IBrush Secondary = new SolidColorBrush(Color.FromRgb(203, 213, 225));
    private static readonly IBrush Muted = new SolidColorBrush(Color.FromRgb(148, 163, 184));
    private static readonly IBrush Emerald = new SolidColorBrush(Color.FromRgb(52, 211, 153));

    public Control Create(IServiceProvider services)
    {
        var hud = services.GetRequiredService<IsleHud>();

        var title = new TextBlock { FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White };
        var gems = new TextBlock { FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = Emerald };
        var status = new TextBlock { FontSize = 13, Foreground = Secondary, Margin = new Thickness(0, 2, 0, 8) };
        var help = new TextBlock
        {
            FontSize = 12,
            Foreground = Muted,
            Text = "A/D or arrows to run · Space to jump\nR to restart · Esc menu"
        };
        var card = Panel(new StackPanel { Children = { title, gems, status, help } }, HorizontalAlignment.Right, VerticalAlignment.Top);

        var hintText = new TextBlock { FontSize = 15, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
        var hint = Panel(hintText, HorizontalAlignment.Center, VerticalAlignment.Bottom);
        hint.MaxWidth = 520;
        hint.Margin = new Thickness(16, 16, 16, 48);

        var summary = new TextBlock { FontSize = 14, Foreground = Secondary, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
        var finish = Panel(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Lighthouse reached!", FontSize = 26, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center },
                summary,
                new TextBlock { Text = "Press Space to play again", FontSize = 13, Foreground = Muted, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) }
            }
        }, HorizontalAlignment.Center, VerticalAlignment.Center);
        finish.Padding = new Thickness(32, 26);
        finish.Width = 420;

        void Refresh(IsleHudState state)
        {
            var time = TimeSpan.FromSeconds(state.Seconds);
            title.Text = state.Title;
            gems.Text = string.Create(CultureInfo.InvariantCulture, $"◆ {state.Gems} / {state.GemsTotal} emeralds");
            status.Text = string.Create(CultureInfo.InvariantCulture, $"{time:m\\:ss} · {state.Falls} {(state.Falls == 1 ? "fall" : "falls")}");
            hintText.Text = state.Hint;
            hint.IsVisible = !string.IsNullOrEmpty(state.Hint) && !state.IsComplete;
            finish.IsVisible = state.IsComplete;
            summary.Text = string.Create(CultureInfo.InvariantCulture,
                $"You found {state.Gems} of {state.GemsTotal} emeralds in {time:m\\:ss}{(state.Gems == state.GemsTotal ? ". Every single one!" : ".")}");
        }

        hud.View.Changed += Refresh;
        Refresh(hud.View.Value);
        return new Panel { IsHitTestVisible = false, Children = { card, hint, finish } };
    }

    private static Border Panel(Control child, HorizontalAlignment horizontal, VerticalAlignment vertical) => new()
    {
        Background = Card,
        BorderBrush = CardBorder,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(16, 12),
        Margin = new Thickness(16),
        HorizontalAlignment = horizontal,
        VerticalAlignment = vertical,
        Child = child
    };
}
