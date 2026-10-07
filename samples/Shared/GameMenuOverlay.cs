using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Avalonia.Hosting;
using Talesmith.Avalonia.Overlays;

namespace Talesmith.Samples.Shared;

/// <summary>The pause menu: resume, settings for display, frame rate and audio, and quitting the game.</summary>
public sealed class GameMenuOverlay : IGameOverlay
{
    private static readonly int[] FrameRateLimits = [0, 30, 60, 75, 120, 144, 165, 240];
    private static readonly IBrush Card = new SolidColorBrush(Color.FromArgb(245, 15, 23, 42));
    private static readonly IBrush CardBorder = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
    private static readonly IBrush Muted = new SolidColorBrush(Color.FromRgb(148, 163, 184));
    private static readonly IBrush Danger = new SolidColorBrush(Color.FromRgb(248, 113, 113));

    public int Order => 100;

    public Control Create(IServiceProvider services) => new MenuView(services.GetRequiredService<GameMenu>());

    private sealed class MenuView : Panel
    {
        private readonly GameMenu _menu;
        private readonly Button _resume;
        private readonly Control _mainPage;
        private readonly Control _settingsPage;
        private readonly ToggleSwitch _fullscreen;
        private readonly ToggleSwitch _vSync;
        private readonly ComboBox _frameRate;
        private readonly ToggleSwitch _showFrameRate;
        private readonly int[] _frameRateLimits;
        private GameHost? _host;
        private bool _refreshing;

        public MenuView(GameMenu menu)
        {
            _menu = menu;
            _frameRateLimits = FrameRateLimits.Contains(menu.MaxFramesPerSecond)
                ? FrameRateLimits
                : [.. FrameRateLimits.Append(menu.MaxFramesPerSecond).Order()];

            _resume = MenuButton("Resume");
            _resume.Classes.Add("accent");
            _resume.Click += (_, _) => _menu.Close();
            var settings = MenuButton("Settings");
            settings.Click += (_, _) => _menu.ShowSettings = true;
            var quit = MenuButton("Quit game");
            quit.Foreground = Danger;
            quit.Click += (_, _) => _menu.Quit();
            _mainPage = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "Paused", FontSize = 26, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 8) },
                    _resume,
                    settings,
                    quit
                }
            };

            _fullscreen = Toggle(value => _menu.Fullscreen = value);
            _vSync = Toggle(value => _menu.VSync = value);
            _showFrameRate = Toggle(value => _menu.ShowFrameRate = value);
            _frameRate = new ComboBox
            {
                ItemsSource = _frameRateLimits.Select(limit => limit == 0 ? "Unlimited" : string.Create(CultureInfo.InvariantCulture, $"{limit} fps")).ToList(),
                MinWidth = 140
            };
            _frameRate.SelectionChanged += (_, _) =>
            {
                if (!_refreshing && _frameRate.SelectedIndex >= 0)
                    _menu.MaxFramesPerSecond = _frameRateLimits[_frameRate.SelectedIndex];
            };
            var back = MenuButton("Back");
            back.Click += (_, _) => _menu.ShowSettings = false;
            _settingsPage = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = "Settings", FontSize = 26, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 4) },
                    Section("Display"),
                    Row("Fullscreen", _fullscreen),
                    Row("VSync", _vSync),
                    Row("Frame rate limit", _frameRate),
                    Hint("With VSync on, the game also never runs faster than your display refreshes."),
                    Row("Show frame rate", _showFrameRate),
                    Section("Audio"),
                    Row("Master volume", VolumeSlider(() => _menu.MasterVolume, value => _menu.MasterVolume = value)),
                    Row("Music", VolumeSlider(() => _menu.MusicVolume, value => _menu.MusicVolume = value)),
                    Row("Effects", VolumeSlider(() => _menu.EffectsVolume, value => _menu.EffectsVolume = value)),
                    new Border { Height = 8 },
                    back
                }
            };

            Background = new SolidColorBrush(Color.FromArgb(150, 2, 6, 23));
            Children.Add(new Border
            {
                Background = Card,
                BorderBrush = CardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(28, 24),
                Width = 440,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new Panel { Children = { _mainPage, _settingsPage } }
            });

            _menu.PropertyChanged += OnMenuChanged;
            Refresh();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _host = this.FindAncestorOfType<GameHost>();
            _host?.AddHandler(PointerPressedEvent, OnHostPointerPressed, RoutingStrategies.Tunnel);
            ApplyFullscreen();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _host?.RemoveHandler(PointerPressedEvent, OnHostPointerPressed);
            _host = null;
            base.OnDetachedFromVisualTree(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key != Key.Escape)
                return;
            if (_menu.ShowSettings)
                _menu.ShowSettings = false;
            else
                _menu.Close();
            e.Handled = true;
        }

        private void OnMenuChanged(object? sender, PropertyChangedEventArgs e)
        {
            var wasOpen = IsVisible;
            Refresh();
            if (e.PropertyName == nameof(GameMenu.Fullscreen))
                ApplyFullscreen();
            if (e.PropertyName == nameof(GameMenu.IsOpen) && wasOpen != _menu.IsOpen)
                Dispatcher.UIThread.Post(_menu.IsOpen ? FocusMenu : FocusGame);
            if (e.PropertyName == nameof(GameMenu.ShowSettings) && _menu.IsOpen)
                Dispatcher.UIThread.Post(FocusMenu);
        }

        private void Refresh()
        {
            _refreshing = true;
            IsVisible = _menu.IsOpen;
            _mainPage.IsVisible = !_menu.ShowSettings;
            _settingsPage.IsVisible = _menu.ShowSettings;
            _fullscreen.IsChecked = _menu.Fullscreen;
            _vSync.IsChecked = _menu.VSync;
            _showFrameRate.IsChecked = _menu.ShowFrameRate;
            _frameRate.SelectedIndex = Array.IndexOf(_frameRateLimits, _menu.MaxFramesPerSecond);
            _refreshing = false;
        }

        private void FocusMenu()
        {
            if (_menu.ShowSettings)
                _fullscreen.Focus(NavigationMethod.Tab);
            else
                _resume.Focus(NavigationMethod.Tab);
        }

        private void FocusGame() => _host?.View.Focus();

        /// <summary>Keeps the open menu modal over the bars around the view, which it does not cover: presses there neither reach the game nor
        /// take focus from the menu.</summary>
        private void OnHostPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!_menu.IsOpen || e.Source is Visual source && (source == this || this.IsVisualAncestorOf(source)))
                return;
            e.Handled = true;
            FocusMenu();
        }

        private void ApplyFullscreen()
        {
            if (TopLevel.GetTopLevel(this) is not Window window)
                return;
            var state = _menu.Fullscreen ? WindowState.FullScreen : WindowState.Normal;
            if (window.WindowState != state)
                window.WindowState = state;
        }

        private ToggleSwitch Toggle(Action<bool> apply)
        {
            var toggle = new ToggleSwitch { OnContent = null, OffContent = null, MinWidth = 0 };
            toggle.IsCheckedChanged += (_, _) =>
            {
                if (!_refreshing)
                    apply(toggle.IsChecked == true);
            };
            return toggle;
        }

        private static Slider VolumeSlider(Func<float> read, Action<float> apply)
        {
            var slider = new Slider { Minimum = 0, Maximum = 100, Value = Math.Round(read() * 100), Width = 160, SmallChange = 5, LargeChange = 10 };
            slider.ValueChanged += (_, e) => apply((float)(e.NewValue / 100));
            return slider;
        }

        private static Button MenuButton(string text) => new()
        {
            Content = text,
            FontSize = 15,
            Padding = new Thickness(12, 10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };

        private static TextBlock Section(string text) => new()
        {
            Text = text.ToUpperInvariant(),
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            LetterSpacing = 1.2,
            Foreground = Muted,
            Margin = new Thickness(0, 12, 0, 2)
        };

        private static TextBlock Hint(string text) => new()
        {
            Text = text,
            FontSize = 12,
            Foreground = Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, -2, 0, 4)
        };

        private static Grid Row(string label, Control control)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = 40 };
            grid.Children.Add(new TextBlock { Text = label, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
            return grid;
        }
    }
}
