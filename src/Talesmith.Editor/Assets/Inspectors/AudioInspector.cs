using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Assets;
using Talesmith.Assets.Database;
using Talesmith.Audio;
using Talesmith.Audio.Decoding;
using Talesmith.Editor.Assets.Previews;

namespace Talesmith.Editor.Assets.Inspectors;

/// <summary>Inspects sounds and music: the waveform with draggable loop points, playback and <see cref="AudioImportSettings"/>.</summary>
public sealed class AudioInspector : IAssetInspector
{
    public IReadOnlyList<AssetKind> Kinds { get; } = [AssetKind.Audio];

    public AssetInspection Inspect(AssetInspectionContext context) => new AudioInspection(context);
}

/// <summary>A sound's waveform, playback and editable <see cref="AudioImportSettings"/>.</summary>
public sealed partial class AudioInspection : AssetInspection
{
    private readonly AudioPreviewPlayer? _player;
    private readonly DispatcherTimer _clock;
    private readonly Stopwatch _played = new();
    private AudioImportSettings _stored = new();
    private short[]? _samples;
    private bool _reading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Info), nameof(DurationText))]
    private Waveform? _waveform;

    [ObservableProperty]
    private double _playhead = -1;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private int _loadTypeIndex;

    [ObservableProperty]
    private bool _loop;

    [ObservableProperty]
    private double _loopStart;

    [ObservableProperty]
    private double _loopEnd;

    [ObservableProperty]
    private int _busIndex;

    [ObservableProperty]
    private double _volume = 1;

    public AudioInspection(AssetInspectionContext context)
        : base(context)
    {
        _player = context.Services.GetService<AudioPreviewPlayer>();
        _clock = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _clock.Tick += (_, _) => Tick();
        RevertCore();
        _ = LoadAsync();
    }

    public override bool HasSettings => true;

    public IReadOnlyList<string> LoadTypes { get; } = ["Auto", "Preload", "Stream"];

    public IReadOnlyList<string> Buses { get; } = [.. Enum.GetNames<AudioBus>()];

    public string Info => Waveform is { } w ? $"{(w.Channels == 1 ? "Mono" : w.Channels == 2 ? "Stereo" : $"{w.Channels} channels")}  ·  {w.SampleRate:N0} Hz" : "Reading…";

    public string DurationText => Waveform is { } w ? w.Duration.ToString("0.00", CultureInfo.CurrentCulture) + " s" : "";

    public override Control CreateView() => new AudioInspectorView { DataContext = this };

    public AudioImportSettings Edited => _stored with
    {
        LoadType = (AudioLoadType)LoadTypeIndex,
        Loop = Loop,
        LoopStart = Math.Max(0, Math.Round(LoopStart, 4)),
        LoopEnd = LoopEnd > 0 ? Math.Round(LoopEnd, 4) : null,
        Bus = (AudioBus)BusIndex,
        Volume = (float)Math.Round(Math.Clamp(Volume, 0, 1), 3)
    };

    [RelayCommand]
    private async Task PlayAsync()
    {
        if (IsPlaying)
        {
            Stop();
            return;
        }

        if (Waveform is not { } waveform)
            return;
        var path = Context.FullPath;
        _samples ??= await Task.Run(() =>
        {
            using var decoder = AudioDecoders.Open(File.OpenRead(path), path);
            return AudioDecoders.ReadToEnd(decoder);
        });
        var settings = Edited;
        var clip = new SoundClip(Asset.Path, waveform.SampleRate, waveform.Channels, _samples)
        {
            Loop = settings.Loop,
            LoopStart = (int)(settings.LoopStart * waveform.SampleRate),
            LoopEnd = settings.LoopEnd is { } end ? (int)(end * waveform.SampleRate) : 0,
            Bus = settings.Bus,
            Volume = settings.Volume
        };
        _player?.Play(clip);
        _played.Restart();
        IsPlaying = true;
        _clock.Start();
    }

    private void Stop()
    {
        _player?.Stop();
        _clock.Stop();
        _played.Reset();
        IsPlaying = false;
        Playhead = -1;
    }

    private void Tick()
    {
        if (Waveform is not { } waveform)
            return;
        var position = _played.Elapsed.TotalSeconds;
        var end = LoopEnd > 0 ? Math.Min(LoopEnd, waveform.Duration) : waveform.Duration;
        if (Loop && position > end && end > LoopStart)
            position = LoopStart + (position - end) % (end - LoopStart);
        if (!Loop && position >= waveform.Duration)
        {
            Stop();
            return;
        }

        Playhead = position;
    }

    protected override async Task<AssetRecord?> ApplyCoreAsync()
    {
        var edited = Edited;
        var record = await Context.Operations.SetImportSettingsAsync(Asset, edited);
        if (record is not null)
            _stored = edited;
        return record;
    }

    protected override void RevertCore()
    {
        try
        {
            _stored = Asset.Meta.GetSettings<AudioImportSettings>();
        }
        catch (AssetException)
        {
            _stored = new AudioImportSettings();
        }

        _reading = true;
        LoadTypeIndex = (int)_stored.LoadType;
        Loop = _stored.Loop;
        LoopStart = _stored.LoopStart;
        LoopEnd = _stored.LoopEnd ?? 0;
        BusIndex = (int)_stored.Bus;
        Volume = _stored.Volume;
        _reading = false;
    }

    protected override void OnContentChanged()
    {
        _samples = null;
        _ = LoadAsync();
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_reading && e.PropertyName is nameof(LoadTypeIndex) or nameof(Loop) or nameof(LoopStart) or nameof(LoopEnd) or nameof(BusIndex) or nameof(Volume))
            IsDirty = Edited != _stored;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && IsPlaying)
            Stop();
        base.Dispose(disposing);
    }

    private async Task LoadAsync()
    {
        var path = Context.FullPath;
        try
        {
            Waveform = await Task.Run(() => Waveform.Read(path));
        }
        catch (Exception ex) when (ex is IOException or AssetException or UnauthorizedAccessException)
        {
            Waveform = null;
        }
    }
}
