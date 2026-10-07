using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Talesmith.Assets;
using Talesmith.Events;
using Talesmith.Rendering;
using Talesmith.Runtime.Hosting;
using Talesmith.Runtime.Scenes;

namespace Talesmith.Avalonia.Loading;

/// <summary>A game's loading screen: its image or title on its background color above a progress bar, or what went wrong when the game
/// could not start.</summary>
/// <remarks>
/// <para>It shows from the moment it is created, before the game exists. <see cref="Follow"/> hands it the game: it then shows how far the
/// start scene has loaded and fades out once the scene is shown. When a later scene change takes more than a moment, it fades back in over
/// the faded-out screen, unless the game's settings turn that off.</para>
/// <para>The look comes from <c>loadingScreen</c> in <c>game.json</c>; see <see cref="LoadingScreenSettings"/>.</para>
/// </remarks>
public sealed partial class LoadingScreen : Panel
{
    private static readonly TimeSpan SceneChangeDelay = TimeSpan.FromSeconds(0.4);
    private static readonly TimeSpan FadeInTime = TimeSpan.FromSeconds(0.2);
    private static readonly TimeSpan FadeOutTime = TimeSpan.FromSeconds(0.3);

    private readonly GameSettings _settings;
    private readonly ILogger? _logger;
    private readonly Grid _loading;
    private readonly TextBlock _title;
    private readonly Image _image;
    private readonly LoadingBar _bar;
    private readonly StackPanel _error;
    private readonly SelectableTextBlock _errorMessage;
    private readonly Button _close;
    private readonly DispatcherTimer _poll;
    private Game? _game;
    private IDisposable? _sceneLoading;
    private DateTime? _loadingSince;
    private bool _sawProgress;
    private bool _isShown = true;

    /// <param name="settings">The game's settings, for its title, colors, image and texture filter.</param>
    /// <param name="assets">Where to read the image from; null shows the title.</param>
    /// <param name="logger">Where to report an image that cannot be shown.</param>
    public LoadingScreen(GameSettings settings, IAssetSource? assets, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _logger = logger;
        var look = settings.LoadingScreen;
        var background = ToAvalonia(look.BackgroundColor ?? settings.ClearColor);
        var foreground = look.ForegroundColor is { } custom ? ToAvalonia(custom) : ContrastingColor(background);
        Background = new SolidColorBrush(background);

        _title = new TextBlock
        {
            Text = settings.Title,
            Foreground = new SolidColorBrush(foreground),
            FontWeight = FontWeight.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsVisible = look.Image is null || assets is null
        };
        _image = new Image { Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Center, IsVisible = false };
        RenderOptions.SetBitmapInterpolationMode(_image,
            settings.TextureFilter == TextureFilter.Nearest ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality);
        _bar = new LoadingBar
        {
            Foreground = new SolidColorBrush(foreground),
            Background = new SolidColorBrush(foreground, 0.18),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 36, 0, 0)
        };
        _loading = new Grid { RowDefinitions = new RowDefinitions("*,Auto,Auto,*") };
        var centerpiece = new Panel { Children = { _title, _image } };
        Grid.SetRow(centerpiece, 1);
        Grid.SetRow(_bar, 2);
        _loading.Children.Add(centerpiece);
        _loading.Children.Add(_bar);

        _errorMessage = new SelectableTextBlock { Foreground = new SolidColorBrush(foreground), Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
        _close = new Button { Content = "Close", MinWidth = 88, HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false };
        _close.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        _error = new StackPanel
        {
            Spacing = 14,
            MaxWidth = 640,
            Margin = new Thickness(32),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
            Children =
            {
                new TextBlock { Text = "The game could not start", FontSize = 22, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(foreground) },
                _errorMessage,
                _close
            }
        };

        Children.Add(_loading);
        Children.Add(_error);
        _poll = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
        _poll.Tick += (_, _) => Poll();
        if (look.Image is { } image && assets is not null)
            _ = ShowImageAsync(assets, image);
    }

    /// <summary>Raised when the player clicks Close under an error; only offered when <see cref="CanClose"/> is set.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Whether an error offers a Close button, as a game's own window does; the editor's Game panel does not.</summary>
    public bool CanClose
    {
        get => _close.IsVisible;
        init => _close.IsVisible = value;
    }

    /// <summary>Shows the progress of <paramref name="game"/>'s scene loads: fades out once its start scene is shown, shows the error when it
    /// could not load, and comes back for slow scene changes.</summary>
    /// <exception cref="InvalidOperationException">The loading screen already follows a game.</exception>
    public void Follow(Game game)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (_game is not null)
            throw new InvalidOperationException("The loading screen already follows a game.");
        _game = game;
        _poll.Start();
        _ = FollowStartAsync(game);
    }

    /// <summary>Replaces the progress with why the game could not start.</summary>
    public void ShowError(string message)
    {
        _poll.Stop();
        _errorMessage.Text = message;
        _loading.IsVisible = false;
        _bar.IsVisible = false;
        _error.IsVisible = true;
        _isShown = true;
        IsVisible = true;
        IsHitTestVisible = true;
        Opacity = 1;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        var size = e.NewSize;
        _title.FontSize = Math.Clamp(size.Height * 0.06, 20, 72);
        _title.MaxWidth = size.Width * 0.8;
        _bar.Width = Math.Clamp(size.Width * 0.3, 160, 420);
        FitImage(size);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _poll.Stop();
        _sceneLoading?.Dispose();
        _sceneLoading = null;
        base.OnDetachedFromVisualTree(e);
    }

    private async Task FollowStartAsync(Game game)
    {
        try
        {
            await game.WhenStarted;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            return;
        }

        _poll.Stop();
        if (_settings.LoadingScreen.BetweenScenes && VisualRoot is not null)
            _sceneLoading = game.Services.GetRequiredService<IEventBus>().Subscribe((ref SceneLoading _) => Dispatcher.UIThread.Post(OnSceneLoading));
        await HideAsync();
    }

    /// <summary>Starts watching a scene change, which shows the loading screen once its loading has taken <see cref="SceneChangeDelay"/>.</summary>
    private void OnSceneLoading()
    {
        if (_game is null || _error.IsVisible)
            return;
        _loadingSince = null;
        _sawProgress = false;
        _poll.Start();
    }

    private void Poll()
    {
        if (_game is not { } game)
            return;
        var scenes = game.Scenes;
        if (!game.WhenStarted.IsCompleted)
        {
            ShowProgress(scenes);
            return;
        }

        if (!scenes.IsLoading)
        {
            _poll.Stop();
            if (_isShown)
                _ = HideAsync();
            return;
        }

        if (scenes.LoadProgress is not null)
            _loadingSince ??= DateTime.UtcNow;
        if (!_isShown && DateTime.UtcNow - _loadingSince >= SceneChangeDelay)
            _ = ShowAsync();
        ShowProgress(scenes);
    }

    /// <summary>Shows the scene's progress; once it was known, the bar stays full while the new scene fades in.</summary>
    private void ShowProgress(ISceneManager scenes)
    {
        if (scenes.LoadProgress is { } progress)
        {
            _sawProgress |= progress.Fraction is not null;
            _bar.Value = progress.Fraction ?? (_sawProgress ? 1 : null);
        }
        else if (_sawProgress)
        {
            _bar.Value = 1;
        }
    }

    private async Task ShowAsync()
    {
        _isShown = true;
        Opacity = 0;
        IsVisible = true;
        IsHitTestVisible = true;
        _bar.IsVisible = true;
        await Fade.ToAsync(this, 1, FadeInTime);
    }

    /// <summary>Fades out; the game underneath takes clicks from the start of the fade.</summary>
    private async Task HideAsync()
    {
        _isShown = false;
        IsHitTestVisible = false;
        await Fade.ToAsync(this, 0, FadeOutTime);
        if (_isShown)
            return;
        IsVisible = false;
        _bar.IsVisible = false;
        _bar.Value = null;
    }

    private async Task ShowImageAsync(IAssetSource assets, string path)
    {
        try
        {
            var bitmap = await Task.Run(() =>
            {
                using var stream = assets.OpenRead(AssetPath.Normalize(path));
                return new Bitmap(stream);
            });
            _image.Source = bitmap;
            _image.IsVisible = true;
            _title.IsVisible = false;
            FitImage(Bounds.Size);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AssetException or ArgumentException or InvalidOperationException)
        {
            if (_logger is not null)
                LogImageFailed(_logger, ex, path);
            _title.IsVisible = true;
        }
    }

    /// <summary>Scales the image to fit most of the screen, by whole steps when the game draws pixel art without smoothing.</summary>
    private void FitImage(Size available)
    {
        if (_image.Source is not { } source || source.Size.Width <= 0 || source.Size.Height <= 0 || available.Width <= 0)
            return;
        var scale = Math.Min(available.Width * 0.7 / source.Size.Width, available.Height * 0.6 / source.Size.Height);
        if (_settings.TextureFilter == TextureFilter.Nearest && scale >= 1)
            scale = Math.Floor(scale);
        _image.Width = source.Size.Width * scale;
        _image.Height = source.Size.Height * scale;
    }

    private static Color ToAvalonia(Mathematics.Color color) => Color.FromArgb(color.A, color.R, color.G, color.B);

    /// <summary>Near-white on dark backgrounds and near-black on light ones.</summary>
    private static Color ContrastingColor(Color background)
    {
        var luminance = (0.2126 * background.R + 0.7152 * background.G + 0.0722 * background.B) / 255;
        return luminance > 0.6 ? Color.FromRgb(24, 24, 28) : Color.FromRgb(242, 242, 245);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The loading screen image {Path} could not be shown; showing the title instead")]
    private static partial void LogImageFailed(ILogger logger, Exception exception, string path);
}
