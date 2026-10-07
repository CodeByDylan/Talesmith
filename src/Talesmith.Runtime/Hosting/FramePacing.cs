namespace Talesmith.Runtime.Hosting;

/// <summary>How often the host runs frames: in step with the display, and optionally capped. Changes apply from the next frame.</summary>
/// <remarks>
/// Starts from <see cref="GameSettings.VSync"/> and <see cref="GameSettings.MaxFramesPerSecond"/>; settings menus change it while the game
/// runs. Safe to use from any thread.
/// </remarks>
public sealed class FramePacing
{
    private volatile bool _vSync;
    private volatile int _maxFramesPerSecond;

    public FramePacing(GameSettings settings)
    {
        _vSync = settings.VSync;
        _maxFramesPerSecond = Math.Max(0, settings.MaxFramesPerSecond);
    }

    /// <summary>Runs one frame per display refresh. When false, frames run as fast as <see cref="MaxFramesPerSecond"/> allows.</summary>
    public bool VSync
    {
        get => _vSync;
        set
        {
            if (_vSync == value)
                return;
            _vSync = value;
            Changed?.Invoke();
        }
    }

    /// <summary>The most frames per second, or 0 for no limit.</summary>
    public int MaxFramesPerSecond
    {
        get => _maxFramesPerSecond;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            if (_maxFramesPerSecond == value)
                return;
            _maxFramesPerSecond = value;
            Changed?.Invoke();
        }
    }

    /// <summary>The shortest time between frames, or zero without a limit.</summary>
    public TimeSpan MinimumFrameTime => _maxFramesPerSecond is > 0 and var max ? TimeSpan.FromSeconds(1.0 / max) : TimeSpan.Zero;

    /// <summary>Raised on the thread that changed <see cref="VSync"/> or <see cref="MaxFramesPerSecond"/>.</summary>
    public event Action? Changed;
}
