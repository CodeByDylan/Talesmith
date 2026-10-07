using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Talesmith.Mathematics;
using Talesmith.UI.Controls;
using Talesmith.VFX;

namespace Talesmith.Editor.Particles.Preview;

/// <summary>Plays particle settings on their own, outside any scene, for previews: playback, speed, replay of one-shot effects and statistics.</summary>
/// <remarks>Advance it with <see cref="AdvanceTo"/> from every view that shows it; it moves once per point in time, so several views can share it.
/// Use it on the UI thread.</remarks>
public sealed partial class ParticlePreviewPlayer : ObservableObject, IDisposable
{
    /// <summary>The longest step taken at once, so a preview that was hidden for a while does not jump.</summary>
    private const double MaxDelta = 0.1;

    private const double StatisticsInterval = 0.2;
    private const double ReplayDelay = 0.6;
    private const int HistoryLength = 90;

    private static readonly ParticleStepContext Origin = new(System.Numerics.Vector2.Zero);

    private readonly ParticleSimulation _simulation = new();
    private long _lastTimestamp;
    private double _statisticsClock;
    private double _finishedFor;
    private int _drawnInstances;
    private bool _disposed;

    [ObservableProperty]
    private double _speed = 1;

    [ObservableProperty]
    private bool _autoReplay = true;

    [ObservableProperty]
    private int _alive;

    [ObservableProperty]
    private float _emittedPerSecond;

    [ObservableProperty]
    private double _simulationMilliseconds;

    [ObservableProperty]
    private int _drawInstances;

    [ObservableProperty]
    private double _budgetUsage;

    public ParticlePreviewPlayer(ParticleSettings? settings = null)
    {
        Settings = settings ?? new ParticleSettings();
        _simulation.Play();
    }

    public ParticleSettings Settings { get; private set; }

    public ParticleSimulation Simulation => _simulation;

    public bool IsPlaying => _simulation.IsPlaying;

    public bool IsPaused => _simulation.IsPaused;

    /// <summary>Seconds since playing started.</summary>
    public double Time => _simulation.Time;

    /// <summary>Alive particles over the last seconds.</summary>
    public ValueHistory AliveHistory { get; } = new(HistoryLength);

    public ValueHistory EmissionHistory { get; } = new(HistoryLength);

    public ValueHistory TimeHistory { get; } = new(HistoryLength);

    /// <summary>The world area the particles and the shape cover, or null when there are none.</summary>
    public Rect2? Bounds => _simulation.AliveCount > 0 ? _simulation.Bounds : null;

    /// <summary>Raised after the simulation moved.</summary>
    public event EventHandler? Advanced;

    /// <summary>Plays other settings; <paramref name="restart"/> clears the particles and starts from the beginning, otherwise they change
    /// as the effect plays, which keeps it smooth while a value is dragged.</summary>
    public void SetSettings(ParticleSettings settings, bool restart)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Settings = settings;
        if (restart)
            Restart();
    }

    public void Play()
    {
        if (_simulation.IsFinished)
            _simulation.Restart();
        else
            _simulation.Play();
        _finishedFor = 0;
        RaiseState();
    }

    public void Pause()
    {
        _simulation.Pause();
        RaiseState();
    }

    public void TogglePause()
    {
        if (_simulation.IsPlaying)
            Pause();
        else
            Play();
    }

    public void Restart()
    {
        _simulation.Restart();
        _finishedFor = 0;
        RaiseState();
    }

    /// <summary>Stops emitting and lets alive particles finish.</summary>
    public void Stop()
    {
        _simulation.Stop();
        RaiseState();
    }

    /// <summary>Emits particles at once.</summary>
    public void Burst(int count)
    {
        _simulation.Emit(count);
        if (_simulation.IsPaused)
            Play();
    }

    /// <summary>Moves the simulation to <paramref name="timestamp"/>, a <see cref="Stopwatch"/> timestamp; repeated calls with one timestamp move it once.</summary>
    public void AdvanceTo(long timestamp)
    {
        if (_disposed)
            return;
        if (_lastTimestamp == 0)
            _lastTimestamp = timestamp;
        var delta = Math.Min(Stopwatch.GetElapsedTime(_lastTimestamp, timestamp).TotalSeconds, MaxDelta);
        if (delta <= 0)
            return;
        _lastTimestamp = timestamp;
        Advance(delta);
    }

    /// <summary>Moves the simulation by <paramref name="seconds"/> of preview time, scaled by <see cref="Speed"/>.</summary>
    public void Advance(double seconds)
    {
        if (_disposed)
            return;
        var step = (float)(seconds * Speed);
        if (step > 0 && !_simulation.IsPaused)
            _simulation.Update(Settings, Origin, step);
        ReplayIfFinished(seconds);
        CollectStatistics(seconds);
        Advanced?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Records how many particles the last frame drew.</summary>
    public void ReportDrawn(int instances) => _drawnInstances = instances;

    public void Dispose()
    {
        _disposed = true;
        _simulation.Dispose();
    }

    private void ReplayIfFinished(double seconds)
    {
        if (!AutoReplay || !_simulation.IsFinished || Settings.Looping)
        {
            _finishedFor = 0;
            return;
        }

        _finishedFor += seconds;
        if (_finishedFor >= ReplayDelay)
            Restart();
    }

    private void CollectStatistics(double seconds)
    {
        _statisticsClock += seconds;
        if (_statisticsClock < StatisticsInterval)
            return;
        _statisticsClock = 0;
        Alive = _simulation.AliveCount;
        EmittedPerSecond = _simulation.EmittedPerSecond;
        SimulationMilliseconds = _simulation.SimulationMilliseconds;
        DrawInstances = _drawnInstances;
        BudgetUsage = Settings.MaxParticles > 0 ? Math.Clamp(_simulation.AliveCount / (double)Settings.MaxParticles, 0, 1) : 0;
        AliveHistory.Push(Alive);
        EmissionHistory.Push(EmittedPerSecond);
        TimeHistory.Push(SimulationMilliseconds);
    }

    private void RaiseState()
    {
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(IsPaused));
    }
}
