using System.Numerics;
using Talesmith.Audio;
using Talesmith.Ecs;
using Talesmith.Events;
using Talesmith.Runtime.Components;
using Talesmith.Systems;

namespace Talesmith.Runtime.Audio;

/// <summary>Plays <see cref="AudioSource"/>s while the game runs and positions spatial ones relative to the listener.</summary>
/// <remarks>
/// <para>Spatial sources need a <see cref="Transform"/>. Their volume falls linearly from full at <see cref="AudioSource.MinDistance"/>
/// to silent at <see cref="AudioSource.MaxDistance"/>, and their pan follows the horizontal offset from the listener, reaching fully
/// left or right at the maximum distance; panning applies to mono clips only. The listener is the entity with an
/// <see cref="AudioListener"/> and a <see cref="Transform"/>, or else the active camera with the highest priority.</para>
/// <para>Volume and pitch changes on a playing source are applied every frame. Sounds stop when their entity is destroyed, their
/// component is removed or the scene stops.</para>
/// </remarks>
[UpdateIn(SystemPhase.LateUpdate)]
[UpdateAfter(typeof(Systems.CameraSystem))]
[ExecuteIn(ExecutionModes.Play)]
public sealed class AudioSourceSystem(IAudioService audio, IEventBus events) : ISystem, ISystemLifecycle
{
    private readonly Dictionary<Entity, Playback> _playing = [];
    private readonly HashSet<Entity> _seen = [];
    private readonly List<Entity> _requestedPlays = [];
    private readonly List<Entity> _requestedStops = [];
    private readonly List<Entity> _scratch = [];
    private IDisposable? _playSubscription;
    private IDisposable? _stopSubscription;

    public void OnStart(World world)
    {
        _playSubscription = events.Subscribe<PlayAudioSource>(OnPlayRequested);
        _stopSubscription = events.Subscribe<StopAudioSource>(OnStopRequested);
    }

    public void OnStop(World world)
    {
        _playSubscription?.Dispose();
        _stopSubscription?.Dispose();
        foreach (var playback in _playing.Values)
            StopPlayback(playback);
        _playing.Clear();
        _seen.Clear();
        _requestedPlays.Clear();
        _requestedStops.Clear();
    }

    public void Update(in SystemContext context)
    {
        var world = context.World;
        StopRemovedSources(world);
        foreach (var entity in _requestedStops)
            StopEntity(entity);
        _requestedStops.Clear();

        var (hasListener, listener) = FindListener(world);
        var job = new SourceJob(this, world, hasListener, listener);
        world.Query<AudioSource>().Run<SourceJob, AudioSource>(ref job);
        _requestedPlays.Clear();
    }

    private void Process(World world, Entity entity, ref AudioSource source, bool hasListener, Vector2 listener)
    {
        var requested = _requestedPlays.Contains(entity);
        var first = _seen.Add(entity);
        if (requested || (first && source.PlayOnStart))
        {
            StopEntity(entity);
            if (Start(world, entity, ref source, hasListener, listener) is { } started)
                _playing[entity] = started;
            return;
        }

        if (!_playing.TryGetValue(entity, out var playback))
            return;

        if (playback.Music is not null)
        {
            if (audio.CurrentMusic != playback.Music)
                _playing.Remove(entity);
            return;
        }

        if (!audio.IsPlaying(playback.Handle))
        {
            _playing.Remove(entity);
            return;
        }

        var (volume, pan) = Spatialize(world, entity, ref source, hasListener, listener);
        if (volume != playback.Volume)
        {
            audio.SetVolume(playback.Handle, volume);
            playback.Volume = volume;
        }

        if (pan != playback.Pan)
        {
            audio.SetPan(playback.Handle, pan);
            playback.Pan = pan;
        }

        if (source.Pitch != playback.Pitch)
        {
            audio.SetPitch(playback.Handle, source.Pitch);
            playback.Pitch = source.Pitch;
        }
    }

    private Playback? Start(World world, Entity entity, ref AudioSource source, bool hasListener, Vector2 listener)
    {
        if (source.Music is { } music)
        {
            audio.PlayMusic(music, source.Loop || music.Loop, volume: Math.Clamp(source.Volume, 0, 1));
            return new Playback { Music = music };
        }

        if (source.Clip is not { } clip)
            return null;

        var (volume, pan) = Spatialize(world, entity, ref source, hasListener, listener);
        var options = new SoundOptions(volume, source.Pitch, pan, source.Loop || clip.Loop, source.Bus ?? clip.Bus);
        var handle = audio.Play(clip, options);
        return handle.IsNone ? null : new Playback { Handle = handle, Volume = volume, Pan = pan, Pitch = source.Pitch };
    }

    private static (float Volume, float Pan) Spatialize(World world, Entity entity, ref AudioSource source, bool hasListener, Vector2 listener)
    {
        var volume = Math.Clamp(source.Volume, 0, 1);
        if (!source.Spatial || !hasListener || !world.TryGet<Transform>(entity, out var transform))
            return (volume, 0);

        var offset = transform.Position - listener;
        var distance = offset.Length();
        var min = Math.Max(source.MinDistance, 0);
        var max = Math.Max(source.MaxDistance, min);
        var attenuation = distance <= min ? 1 : distance >= max ? 0 : 1 - (distance - min) / (max - min);
        var pan = max <= 0 ? 0 : Math.Clamp(offset.X / max, -1, 1);
        return (volume * attenuation, pan);
    }

    private static (bool Found, Vector2 Position) FindListener(World world)
    {
        foreach (var archetype in world.Query<AudioListener, Transform>())
        {
            if (archetype.Count > 0)
                return (true, archetype.GetSpan<Transform>()[0].Position);
        }

        var found = false;
        var best = int.MinValue;
        var position = Vector2.Zero;
        foreach (var archetype in world.Query<Camera>())
        {
            foreach (ref readonly var camera in archetype.GetSpan<Camera>())
            {
                if (!camera.Active || (found && camera.Priority <= best))
                    continue;
                found = true;
                best = camera.Priority;
                position = camera.View.Position;
            }
        }

        return (found, position);
    }

    private void StopRemovedSources(World world)
    {
        _scratch.Clear();
        foreach (var entity in _playing.Keys)
        {
            if (!world.IsAlive(entity) || !world.Has<AudioSource>(entity))
                _scratch.Add(entity);
        }

        foreach (var entity in _scratch)
            StopEntity(entity);

        if (_seen.Count > _playing.Count)
            _seen.RemoveWhere(entity => !world.IsAlive(entity) || !world.Has<AudioSource>(entity));
    }

    private void StopEntity(Entity entity)
    {
        if (_playing.Remove(entity, out var playback))
            StopPlayback(playback);
    }

    private void StopPlayback(Playback playback)
    {
        if (playback.Music is not null)
        {
            if (audio.CurrentMusic == playback.Music)
                audio.StopMusic();
        }
        else
        {
            audio.Stop(playback.Handle);
        }
    }

    private void OnPlayRequested(ref PlayAudioSource e) => _requestedPlays.Add(e.Entity);

    private void OnStopRequested(ref StopAudioSource e) => _requestedStops.Add(e.Entity);

    private sealed class Playback
    {
        public SoundHandle Handle { get; init; }

        public MusicTrack? Music { get; init; }

        public float Volume { get; set; }

        public float Pan { get; set; }

        public float Pitch { get; set; }
    }

    private readonly struct SourceJob(AudioSourceSystem system, World world, bool hasListener, Vector2 listener) : IForEach<AudioSource>
    {
        public void Execute(Entity entity, ref AudioSource source) => system.Process(world, entity, ref source, hasListener, listener);
    }
}
