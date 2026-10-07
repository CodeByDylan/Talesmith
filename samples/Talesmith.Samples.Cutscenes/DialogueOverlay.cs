using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Avalonia.Overlays;

namespace Talesmith.Samples.Cutscenes;

/// <summary>The dialogue box at the bottom of the screen: speaker, the line as it is revealed, and choices.</summary>
public sealed class DialogueOverlay : IGameOverlay
{
    private static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(251, 191, 36));
    private static readonly IBrush Muted = new SolidColorBrush(Color.FromRgb(148, 163, 184));
    private static readonly IBrush Selected = new SolidColorBrush(Color.FromArgb(70, 251, 191, 36));

    public int Order => 10;

    public Control Create(IServiceProvider services)
    {
        var dialogue = services.GetRequiredService<DialogueState>();
        var speaker = new TextBlock { FontSize = 15, FontWeight = FontWeight.Bold, Foreground = Accent };
        var text = new TextBlock { FontSize = 18, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), LineHeight = 26 };
        var options = new StackPanel { Spacing = 4, Margin = new Thickness(0, 12, 0, 0) };
        var hint = new TextBlock { FontSize = 12, Foreground = Muted, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var box = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(232, 15, 23, 42)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(90, 251, 191, 36)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(24, 18),
            Margin = new Thickness(24),
            MaxWidth = 820,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false,
            Opacity = 0,
            Transitions = [new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(180) }],
            Child = new StackPanel { Children = { speaker, text, options, hint } }
        };

        void Refresh(DialogueView view)
        {
            box.Opacity = view.IsOpen ? 1 : 0;
            speaker.Text = view.Speaker;
            text.Text = view.VisibleText;
            options.Children.Clear();
            if (view.IsFullyRevealed)
            {
                for (var i = 0; i < view.Options.Count; i++)
                {
                    options.Children.Add(new Border
                    {
                        Background = i == view.SelectedOption ? Selected : Brushes.Transparent,
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(10, 5),
                        Child = new TextBlock { Text = $"{i + 1}.  {view.Options[i]}", FontSize = 16, Foreground = Brushes.White }
                    });
                }
            }

            hint.Text = !view.IsFullyRevealed ? "E to skip" : view.Options.Count > 0 ? "↑ ↓ to choose · E to confirm" : "E to continue";
        }

        dialogue.View.Changed += Refresh;
        Refresh(dialogue.View.Value);
        return box;
    }
}
