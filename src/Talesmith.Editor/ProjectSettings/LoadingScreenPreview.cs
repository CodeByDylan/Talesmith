using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Talesmith.Assets;
using Talesmith.Avalonia.Loading;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Editor.ProjectSettings;

/// <summary>A small copy of the game's loading screen as a 1280×720 window would show it, rebuilt when its settings change.</summary>
public sealed class LoadingScreenPreview : Border
{
    public static readonly StyledProperty<GameSettings?> SettingsProperty =
        AvaloniaProperty.Register<LoadingScreenPreview, GameSettings?>(nameof(Settings));

    public static readonly StyledProperty<IAssetSource?> AssetsProperty =
        AvaloniaProperty.Register<LoadingScreenPreview, IAssetSource?>(nameof(Assets));

    private bool _rebuildQueued;

    public LoadingScreenPreview() => ClipToBounds = true;

    public GameSettings? Settings
    {
        get => GetValue(SettingsProperty);
        set => SetValue(SettingsProperty, value);
    }

    /// <summary>Where the loading screen's image is read from.</summary>
    public IAssetSource? Assets
    {
        get => GetValue(AssetsProperty);
        set => SetValue(AssetsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SettingsProperty || change.Property == AssetsProperty)
            QueueRebuild();
    }

    private void QueueRebuild()
    {
        if (_rebuildQueued)
            return;
        _rebuildQueued = true;
        Dispatcher.UIThread.Post(Rebuild, DispatcherPriority.Background);
    }

    /// <summary>Replaces the screen once per burst of changes, such as while a color is dragged.</summary>
    private void Rebuild()
    {
        _rebuildQueued = false;
        Child = Settings is { } settings
            ? new Viewbox { Stretch = Stretch.Uniform, Child = new LoadingScreen(settings, Assets) { Width = 1280, Height = 720 } }
            : null;
    }
}
