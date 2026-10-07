using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Talesmith.Audio;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Samples.Shared;

/// <summary>The pause menu and the player's settings: opening it pauses the game, and every change applies immediately.</summary>
/// <remarks>
/// <para>The menu is the overlay's model and belongs to the UI thread, like its properties and <see cref="PropertyChanged"/>. Game code
/// opens it with <see cref="RequestOpen"/>; the menu pauses the game and applies audio settings through <see cref="Game.Post"/>.</para>
/// <para>Preferences are saved when the menu closes and when the player quits, in a file named after the game's title.</para>
/// </remarks>
public sealed partial class GameMenu : INotifyPropertyChanged
{
    private readonly Game _game;
    private readonly FramePacing _pacing;
    private readonly IAudioService _audio;
    private readonly GameLifetime _lifetime;
    private readonly IGameUi _ui;
    private readonly ILogger<GameMenu> _logger;
    private readonly string _preferencesPath;
    private readonly Action _open;
    private PlayerPreferences _preferences;
    private bool _isOpen;
    private bool _showSettings;

    /// <summary>Whether the game was paused before the menu opened; read and written on the game thread only.</summary>
    private bool _wasPaused;

    public GameMenu(Game game, GameSettings settings, FramePacing pacing, IAudioService audio, GameLifetime lifetime, IGameUi ui, ILogger<GameMenu> logger)
    {
        _game = game;
        _pacing = pacing;
        _audio = audio;
        _lifetime = lifetime;
        _ui = ui;
        _logger = logger;
        _open = Open;
        _preferencesPath = PlayerPreferences.FilePathFor(settings.Title);
        _preferences = PlayerPreferences.Load(_preferencesPath) ?? new PlayerPreferences { VSync = pacing.VSync, MaxFramesPerSecond = pacing.MaxFramesPerSecond };
        _pacing.VSync = _preferences.VSync;
        _pacing.MaxFramesPerSecond = _preferences.MaxFramesPerSecond;
        var preferences = _preferences;
        _game.Post(() =>
        {
            _audio.MasterVolume = preferences.MasterVolume;
            _audio.SetBusVolume(AudioBus.Music, preferences.MusicVolume);
            _audio.SetBusVolume(AudioBus.Effects, preferences.EffectsVolume);
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsOpen
    {
        get => _isOpen;
        private set => Set(ref _isOpen, value);
    }

    /// <summary>Whether the menu shows the settings page rather than the main page.</summary>
    public bool ShowSettings
    {
        get => _showSettings;
        set => Set(ref _showSettings, value);
    }

    public bool Fullscreen
    {
        get => _preferences.Fullscreen;
        set => Update(_preferences with { Fullscreen = value });
    }

    public bool VSync
    {
        get => _preferences.VSync;
        set
        {
            _pacing.VSync = value;
            Update(_preferences with { VSync = value });
        }
    }

    public int MaxFramesPerSecond
    {
        get => _preferences.MaxFramesPerSecond;
        set
        {
            _pacing.MaxFramesPerSecond = value;
            Update(_preferences with { MaxFramesPerSecond = value });
        }
    }

    public bool ShowFrameRate
    {
        get => _preferences.ShowFrameRate;
        set => Update(_preferences with { ShowFrameRate = value });
    }

    public float MasterVolume
    {
        get => _preferences.MasterVolume;
        set
        {
            _game.Post(() => _audio.MasterVolume = value);
            Update(_preferences with { MasterVolume = value });
        }
    }

    public float MusicVolume
    {
        get => _preferences.MusicVolume;
        set
        {
            _game.Post(() => _audio.SetBusVolume(AudioBus.Music, value));
            Update(_preferences with { MusicVolume = value });
        }
    }

    public float EffectsVolume
    {
        get => _preferences.EffectsVolume;
        set
        {
            _game.Post(() => _audio.SetBusVolume(AudioBus.Effects, value));
            Update(_preferences with { EffectsVolume = value });
        }
    }

    /// <summary>Opens the menu from game code; safe to call from any thread.</summary>
    public void RequestOpen() => _ui.Post(_open);

    public void Open()
    {
        if (IsOpen)
            return;
        _game.Post(() =>
        {
            _wasPaused = _game.IsPaused;
            _game.IsPaused = true;
        });
        ShowSettings = false;
        IsOpen = true;
    }

    public void Close()
    {
        if (!IsOpen)
            return;
        IsOpen = false;
        _game.Post(() => _game.IsPaused = _wasPaused);
        Save();
    }

    public void Quit()
    {
        Save();
        _lifetime.Quit();
    }

    private void Update(PlayerPreferences preferences, [CallerMemberName] string? name = null)
    {
        if (preferences == _preferences)
            return;
        _preferences = preferences;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private void Save()
    {
        try
        {
            _preferences.Save(_preferencesPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogSaveFailed(_logger, ex, _preferencesPath);
        }
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Settings could not be saved to {Path}")]
    private static partial void LogSaveFailed(ILogger logger, Exception exception, string path);
}
