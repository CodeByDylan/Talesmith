using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Talesmith.Mathematics;
using Talesmith.VFX.Simulation;

namespace Talesmith.VFX;

/// <summary>Whether an emitter is playing.</summary>
public enum ParticlePlayState
{
    /// <summary>Not emitting; particles already alive keep moving until they die.</summary>
    Stopped,

    Playing,

    /// <summary>Frozen: nothing moves or emits until played again.</summary>
    Paused
}

/// <summary>What <see cref="ParticleSimulation.Stop"/> does with particles that are alive.</summary>
public enum ParticleStopBehavior
{
    /// <summary>Lets alive particles finish their lives.</summary>
    StopEmitting,

    /// <summary>Removes every particle at once.</summary>
    StopEmittingAndClear
}

/// <summary>The running state of one emitter: its particles, playback, timers and random numbers.</summary>
/// <remarks>
/// <para><see cref="Update"/> advances time with a variable step split into sub-steps of at most <see cref="MaxStep"/>, so motion looks
/// smooth at any frame rate and stays stable during hitches. Particles are emitted at their exact time within a step and moved for the
/// rest of it, and a moving emitter spreads them along its path, so streams never clump into frame-sized bands.</para>
/// <para>Playback is deterministic: with a fixed <see cref="ParticleSettings.Seed"/>, the same updates produce the same particles.</para>
/// </remarks>
public sealed partial class ParticleSimulation : IDisposable
{
    /// <summary>The longest sub-step in seconds; longer updates are split.</summary>
    public const float MaxStep = 1f / 30;

    private const int MaxPrewarmSteps = 600;

    private readonly ParticleBuffer _particles = new();
    private readonly ShapeSampler _shape = new();
    private readonly ParticleModuleContext _modules;
    private readonly ParticleCollisionContext _collisions;
    private readonly CurveTable _sizeTable = new();
    private readonly CurveTable _speedTable = new();
    private readonly CurveTable _linearTable = new();
    private readonly CurveTable _rotationTable = new();
    private readonly CurveTable _noiseTable = new();
    private readonly GradientTable _colorTable = new();
    private int[] _burstsFired = [];
    private float _cycleTime;
    private float _delay;
    private float _rateAccumulator;
    private float _distanceAccumulator;
    private int _pendingEmit;
    private bool _cyclePending;
    private bool _prewarmPending;
    private bool _started;
    private Vector2 _lastPosition;
    private bool _hasLastPosition;
    private int _emittedThisUpdate;

    public ParticleSimulation()
    {
        _modules = new ParticleModuleContext(_particles);
        _collisions = new ParticleCollisionContext(_particles);
    }

    public ParticlePlayState State { get; private set; }

    public bool IsPlaying => State == ParticlePlayState.Playing;

    public bool IsPaused => State == ParticlePlayState.Paused;

    /// <summary>Whether the current cycle is still emitting; false after a non-looping cycle ends or after <see cref="Stop"/>.</summary>
    public bool IsEmitting { get; private set; }

    /// <summary>Whether playback has finished: nothing is emitting, waiting to be emitted or alive.</summary>
    public bool IsFinished => State != ParticlePlayState.Paused && !IsEmitting && !_cyclePending && _pendingEmit == 0 && _particles.Count == 0;

    public int AliveCount => _particles.Count;

    /// <summary>The alive particles, for plugin modules, tools and tests.</summary>
    public ParticleBuffer Particles => _particles;

    /// <summary>Seconds simulated since playing started, including prewarm.</summary>
    public double Time { get; private set; }

    /// <summary>The settings simulated by the last update.</summary>
    public ParticleSettings? Settings { get; private set; }

    /// <summary>World bounds of the alive particles, including their size, after the last update.</summary>
    public Rect2 Bounds { get; private set; }

    /// <summary>Particles emitted per second, smoothed over about half a second.</summary>
    public float EmittedPerSecond { get; private set; }

    /// <summary>Time the last update took.</summary>
    public double SimulationMilliseconds { get; private set; }

    /// <summary>The level-of-detail and budget multiplier the last update applied to emission.</summary>
    public float EmissionScale { get; private set; } = 1;

    /// <summary>Whether the emitter was within the camera's view at its last update.</summary>
    public bool IsVisible { get; internal set; } = true;

    /// <summary>Collisions recorded by the last update, published by the simulation system.</summary>
    internal ReadOnlySpan<ParticleCollision> CollisionEvents => _collisions.Events;

    /// <summary>Particles emitted by the last update.</summary>
    public int LastEmitted => _emittedThisUpdate;

    /// <summary>Starts emitting from the beginning of a cycle, or resumes after <see cref="Pause"/>.</summary>
    public void Play()
    {
        _started = true;
        if (State == ParticlePlayState.Paused)
        {
            State = ParticlePlayState.Playing;
            return;
        }

        if (State == ParticlePlayState.Playing && (IsEmitting || _cyclePending))
            return;
        State = ParticlePlayState.Playing;
        _cyclePending = true;
        _prewarmPending = true;
    }

    /// <summary>Stops emitting; alive particles finish their lives unless <paramref name="behavior"/> clears them.</summary>
    public void Stop(ParticleStopBehavior behavior = ParticleStopBehavior.StopEmitting)
    {
        _started = true;
        State = ParticlePlayState.Stopped;
        IsEmitting = false;
        _cyclePending = false;
        _prewarmPending = false;
        if (behavior == ParticleStopBehavior.StopEmittingAndClear)
            Clear();
    }

    /// <summary>Freezes particles and emission until <see cref="Play"/> is called.</summary>
    public void Pause()
    {
        _started = true;
        if (State == ParticlePlayState.Playing)
            State = ParticlePlayState.Paused;
    }

    /// <summary>Removes every particle without changing playback.</summary>
    public void Clear()
    {
        _particles.Clear();
        _pendingEmit = 0;
    }

    /// <summary>Clears every particle and plays from the beginning, with the same random numbers when the seed is fixed.</summary>
    public void Restart()
    {
        Clear();
        IsEmitting = false;
        State = ParticlePlayState.Stopped;
        Play();
    }

    /// <summary>Emits particles at once on the next update, whether or not the emitter is playing.</summary>
    public void Emit(int count)
    {
        if (count > 0)
            _pendingEmit += count;
    }

    /// <summary>Advances the simulation by <paramref name="deltaTime"/> seconds of game time.</summary>
    public void Update(ParticleSettings settings, in ParticleStepContext context, float deltaTime)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var started = Stopwatch.GetTimestamp();
        Settings = settings;
        EmissionScale = Math.Clamp(context.EmissionScale, 0, 1);
        _emittedThisUpdate = 0;
        _collisions.ClearEvents();
        _collisions.MaxEvents = context.MaxCollisionEvents;
        if (!_started)
        {
            _started = true;
            if (settings.PlayOnStart)
                Play();
        }

        if (State != ParticlePlayState.Paused)
        {
            Prepare(settings);
            if (!_hasLastPosition)
            {
                _lastPosition = context.Position;
                _hasLastPosition = true;
            }

            if (_cyclePending)
                BeginCycle(settings);
            if (_prewarmPending)
            {
                _prewarmPending = false;
                if (settings.Prewarm && settings.Looping)
                    Prewarm(settings, context);
            }

            var delta = deltaTime * MathF.Max(0, settings.SimulationSpeed);
            if (delta > 0)
            {
                var steps = Math.Max(1, (int)MathF.Ceiling(delta / MaxStep));
                var step = delta / steps;
                for (var i = 0; i < steps; i++)
                {
                    var from = Vector2.Lerp(_lastPosition, context.Position, i / (float)steps);
                    var to = Vector2.Lerp(_lastPosition, context.Position, (i + 1) / (float)steps);
                    Step(settings, context, step, from, to);
                }
            }
            else if (_pendingEmit > 0)
            {
                Step(settings, context, 0, context.Position, context.Position);
            }

            _lastPosition = context.Position;
            if (State == ParticlePlayState.Playing && !IsEmitting && _particles.Count == 0)
                State = ParticlePlayState.Stopped;
        }

        UpdateBounds(settings, context);
        if (deltaTime > 0)
        {
            var rate = _emittedThisUpdate / deltaTime;
            EmittedPerSecond = MathHelper.Lerp(EmittedPerSecond, rate, 1 - MathF.Exp(-deltaTime / 0.5f));
        }

        SimulationMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    /// <summary>The distance from the emitter to the furthest point its shape emits from, in the emitter's unscaled space.</summary>
    public float ShapeExtent(ParticleSettings settings) => _shape.Extent(settings.Shape);

    public void Dispose() => _particles.Dispose();

    private void Prepare(ParticleSettings settings)
    {
        _shape.Prepare(settings.Shape);
        _sizeTable.Update(settings.SizeOverLifetime.Size);
        _speedTable.Update(settings.VelocityOverLifetime.SpeedMultiplier);
        _linearTable.Update(settings.VelocityOverLifetime.LinearOverLifetime);
        _rotationTable.Update(settings.RotationOverLifetime.OverLifetime);
        _noiseTable.Update(settings.Noise.StrengthOverLifetime);
        _colorTable.Update(settings.ColorOverLifetime.Color);
        if (_burstsFired.Length != settings.Emission.Bursts.Count)
            Array.Resize(ref _burstsFired, settings.Emission.Bursts.Count);
    }

    private void BeginCycle(ParticleSettings settings)
    {
        _cyclePending = false;
        IsEmitting = true;
        _cycleTime = 0;
        Time = 0;
        _delay = MathF.Max(0, settings.StartDelay);
        _rateAccumulator = 0;
        _distanceAccumulator = 0;
        Array.Clear(_burstsFired);
        var seed = settings.Seed != 0 ? (ulong)(uint)settings.Seed : (ulong)Random.Shared.NextInt64();
        _modules.RandomState = new ParticleRandom(seed);
    }

    private void Prewarm(ParticleSettings settings, in ParticleStepContext context)
    {
        var duration = MathF.Max(settings.Duration, 0.05f);
        var steps = Math.Clamp((int)MathF.Ceiling(duration / MaxStep), 1, MaxPrewarmSteps);
        var step = duration / steps;
        for (var i = 0; i < steps; i++)
            Step(settings, context, step, context.Position, context.Position);
        _emittedThisUpdate = 0;
    }

    private void Step(ParticleSettings settings, in ParticleStepContext context, float step, Vector2 from, Vector2 to)
    {
        if (_particles.Count > 0 && step > 0)
            Advance(settings, context, step, to);

        var removed = _particles.RemoveDead();
        context.Budget?.Release(removed);
        if (_particles.Count == 0)
            _rotated = false;
        EmitScheduled(settings, context, step, from, to);
        Time += step;
    }

    private void UpdateBounds(ParticleSettings settings, in ParticleStepContext context)
    {
        var count = _particles.Count;
        if (count == 0)
        {
            Bounds = new Rect2(context.Position.X, context.Position.Y, 0, 0);
            return;
        }

        MinMax(_particles.PositionXArray.AsSpan(0, count), out var minX, out var maxX);
        MinMax(_particles.PositionYArray.AsSpan(0, count), out var minY, out var maxY);
        var local = settings.SimulationSpace == ParticleSimulationSpace.Local;
        var sizeScale = local ? 1 : AverageScale(context.Scale);
        var size = settings.Initial.Size.LargestMagnitude() * sizeScale;
        if (settings.SizeOverLifetime.Enabled)
            size *= MathF.Max(MathF.Abs(_sizeTable.Min), MathF.Abs(_sizeTable.Max));
        if (settings.Renderer.Alignment == ParticleAlignment.Stretch)
        {
            var speed = MathF.Sqrt(MaxSpeedSquared(count));
            size *= MathF.Max(1, settings.Renderer.StretchLengthScale + speed * settings.Renderer.StretchSpeedScale);
        }

        var bounds = Rect2.FromEdges(minX, minY, maxX, maxY).Inflate(size * 0.75f);
        Bounds = local ? bounds.Transform(context.LocalToWorld) : bounds;
    }

    private float MaxSpeedSquared(int count)
    {
        var vx = _particles.VelocityXArray.AsSpan(0, count);
        var vy = _particles.VelocityYArray.AsSpan(0, count);
        var max = 0f;
        var i = 0;
        if (Vector.IsHardwareAccelerated && count >= Vector<float>.Count)
        {
            var vxs = MemoryMarshal.Cast<float, Vector<float>>(vx);
            var vys = MemoryMarshal.Cast<float, Vector<float>>(vy);
            var best = Vector<float>.Zero;
            for (var k = 0; k < vxs.Length; k++)
                best = Vector.Max(best, vxs[k] * vxs[k] + vys[k] * vys[k]);
            for (var lane = 0; lane < Vector<float>.Count; lane++)
                max = MathF.Max(max, best[lane]);
            i = vxs.Length * Vector<float>.Count;
        }

        for (; i < count; i++)
            max = MathF.Max(max, vx[i] * vx[i] + vy[i] * vy[i]);
        return max;
    }

    private static void MinMax(ReadOnlySpan<float> values, out float min, out float max)
    {
        min = float.MaxValue;
        max = float.MinValue;
        var i = 0;
        if (Vector.IsHardwareAccelerated && values.Length >= Vector<float>.Count)
        {
            var vectors = MemoryMarshal.Cast<float, Vector<float>>(values);
            var low = new Vector<float>(float.MaxValue);
            var high = new Vector<float>(float.MinValue);
            foreach (var vector in vectors)
            {
                low = Vector.Min(low, vector);
                high = Vector.Max(high, vector);
            }

            for (var lane = 0; lane < Vector<float>.Count; lane++)
            {
                min = MathF.Min(min, low[lane]);
                max = MathF.Max(max, high[lane]);
            }

            i = vectors.Length * Vector<float>.Count;
        }

        for (; i < values.Length; i++)
        {
            min = MathF.Min(min, values[i]);
            max = MathF.Max(max, values[i]);
        }
    }

    private static float AverageScale(Vector2 scale) => (MathF.Abs(scale.X) + MathF.Abs(scale.Y)) * 0.5f;
}
