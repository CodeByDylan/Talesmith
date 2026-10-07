using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Talesmith.Audio;
using Talesmith.Audio.OpenAL;

namespace Talesmith.Editor.Assets.Previews;

/// <summary>Plays sounds in the editor, such as when previewing audio assets; the edit game itself is silent.</summary>
/// <remarks>The audio device opens the first time something plays. Without a device, playback is silent but still reports its position.</remarks>
public sealed partial class AudioPreviewPlayer(ILogger<AudioPreviewPlayer> logger) : IDisposable
{
    private IAudioService? _audio;
    private DispatcherTimer? _timer;
    private SoundHandle _sound;

    /// <summary>Whether the last sound is still playing.</summary>
    public bool IsPlaying => _sound is { IsNone: false } sound && Audio.IsPlaying(sound);

    private IAudioService Audio => _audio ??= Open();

    public void Play(SoundClip clip, float volume = 1)
    {
        ArgumentNullException.ThrowIfNull(clip);
        Stop();
        _sound = Audio.Play(clip, new SoundOptions(Volume: volume, Loop: clip.Loop, Bus: clip.Bus));
        if (_timer is null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
            _timer.Tick += (_, _) => Audio.Update(0.05f);
        }

        _timer.Start();
    }

    public void Stop()
    {
        if (!_sound.IsNone)
            Audio.Stop(_sound);
        _sound = default;
        _timer?.Stop();
    }

    public void Dispose()
    {
        _timer?.Stop();
        _audio?.Dispose();
    }

    private IAudioService Open()
    {
        try
        {
            return new OpenALAudioService();
        }
        catch (Exception ex) when (ex is AudioDeviceException or DllNotFoundException or EntryPointNotFoundException)
        {
            LogNoDevice(logger, ex);
            return new SilentAudioService();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "No audio device for previews, so they are silent")]
    private static partial void LogNoDevice(ILogger logger, Exception exception);
}
