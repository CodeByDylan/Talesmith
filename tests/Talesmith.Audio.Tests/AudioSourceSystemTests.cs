using System.Numerics;
using Talesmith.Ecs;
using Talesmith.Events;
using Talesmith.Runtime.Audio;
using Talesmith.Runtime.Components;
using Talesmith.Systems;
using Talesmith.Time;

namespace Talesmith.Audio.Tests;

public sealed class AudioSourceSystemTests : IDisposable
{
    private static readonly SoundClip Clip = new("hit.wav", 1000, 1, new short[1000]);

    private readonly RecordingAudio _audio = new();
    private readonly EventBus _events = new();
    private readonly World _world = new();
    private readonly AudioSourceSystem _system;

    public AudioSourceSystemTests()
    {
        _system = new AudioSourceSystem(_audio, _events);
        _system.OnStart(_world);
    }

    public void Dispose() => _audio.Dispose();

    [Fact]
    public void SourcesPlayOnceWhenTheyAppear()
    {
        _world.Create(new AudioSource { Clip = Clip, Volume = 0.5f, Pitch = 1.5f });
        _world.Create(new AudioSource { Clip = Clip, PlayOnStart = false });

        Tick();
        Tick();

        var played = Assert.Single(_audio.Played);
        Assert.Equal(0.5f, played.Options.Volume);
        Assert.Equal(1.5f, played.Options.Pitch);
        Assert.Equal(AudioBus.Effects, played.Options.Bus);
    }

    [Fact]
    public void TheBusAndLoopComeFromTheClipUnlessTheSourceSetsThem()
    {
        var music = Clip with { Loop = true, Bus = AudioBus.Voice };
        _world.Create(new AudioSource { Clip = music });
        _world.Create(new AudioSource { Clip = music, Bus = AudioBus.Interface });

        Tick();

        Assert.Equal([AudioBus.Voice, AudioBus.Interface], _audio.Played.Select(play => play.Options.Bus));
        Assert.All(_audio.Played, play => Assert.True(play.Options.Loop));
    }

    [Fact]
    public void SpatialSourcesFadeWithDistanceAndPanTowardsTheirSide()
    {
        _world.Create(new AudioListener(), new Transform(Vector2.Zero));
        var near = new AudioSource { Clip = Clip, Spatial = true, MinDistance = 100, MaxDistance = 500 };
        _world.Create(near, new Transform(new Vector2(-50, 0)));
        _world.Create(near, new Transform(new Vector2(300, 0)));
        _world.Create(near, new Transform(new Vector2(0, 900)));

        Tick();

        Assert.Equal([1f, 0.5f, 0f], _audio.Played.Select(play => play.Options.Volume));
        Assert.Equal([-0.1f, 0.6f, 0f], _audio.Played.Select(play => play.Options.Pan));
    }

    [Fact]
    public void TheActiveCameraHearsWhenThereIsNoListener()
    {
        _world.Create(new Camera(new Vector2(1000, 0)) { Priority = 1 });
        _world.Create(new Camera(new Vector2(0, 0)) { Active = false, Priority = 5 });
        _world.Create(new AudioSource { Clip = Clip, Spatial = true, MinDistance = 0, MaxDistance = 1000 }, new Transform(new Vector2(500, 0)));

        Tick();

        Assert.Equal(0.5f, Assert.Single(_audio.Played).Options.Volume);
    }

    [Fact]
    public void MovingSourcesUpdateTheirVolume()
    {
        _world.Create(new AudioListener(), new Transform(Vector2.Zero));
        var entity = _world.Create(new AudioSource { Clip = Clip, Loop = true, Spatial = true, MinDistance = 0, MaxDistance = 100 }, new Transform(Vector2.Zero));
        Tick();

        _world.Get<Transform>(entity).Position = new Vector2(0, 50);
        Tick();

        Assert.Equal(0.5f, _audio.Volumes[_audio.Played[0].Handle]);
    }

    [Fact]
    public void DestroyingTheEntityStopsItsSound()
    {
        var entity = _world.Create(new AudioSource { Clip = Clip, Loop = true });
        Tick();

        _world.Destroy(entity);
        Tick();

        Assert.Equal([_audio.Played[0].Handle], _audio.Stopped);
    }

    [Fact]
    public void StoppingTheSceneStopsEverySound()
    {
        _world.Create(new AudioSource { Clip = Clip, Loop = true });
        _world.Create(new AudioSource { Music = new MusicTrack("theme.ogg", () => throw new InvalidOperationException()) });
        Tick();

        _system.OnStop(_world);

        Assert.Single(_audio.Stopped);
        Assert.Null(_audio.CurrentMusic);
    }

    [Fact]
    public void EventsPlayAndStopSources()
    {
        var entity = _world.Create(new AudioSource { Clip = Clip, PlayOnStart = false, Loop = true });
        Tick();

        _events.Publish(new PlayAudioSource(entity));
        Tick();
        Assert.Single(_audio.Played);

        _events.Publish(new StopAudioSource(entity));
        Tick();
        Assert.Single(_audio.Stopped);
    }

    private void Tick() => _system.Update(new SystemContext(_world, new GameTime(1 / 60f, 0, 1 / 60f, 0, 0, 0), new CommandBuffer(_world)));

    private sealed class RecordingAudio : IAudioService
    {
        private int _next;

        public List<(SoundHandle Handle, SoundOptions Options)> Played { get; } = [];

        public List<SoundHandle> Stopped { get; } = [];

        public Dictionary<SoundHandle, float> Volumes { get; } = [];

        public string DeviceName => "Test";

        public float MasterVolume { get; set; } = 1;

        public bool IsPaused { get; set; }

        public int ActiveSounds => Played.Count - Stopped.Count;

        public MusicTrack? CurrentMusic { get; private set; }

        public float GetBusVolume(AudioBus bus) => 1;

        public void SetBusVolume(AudioBus bus, float volume)
        {
        }

        public SoundHandle Play(SoundClip clip, SoundOptions? options = null)
        {
            var handle = new SoundHandle(++_next);
            Played.Add((handle, options ?? SoundOptions.For(clip)));
            return handle;
        }

        public void Stop(SoundHandle sound) => Stopped.Add(sound);

        public bool IsPlaying(SoundHandle sound) => !Stopped.Contains(sound);

        public void SetVolume(SoundHandle sound, float volume) => Volumes[sound] = volume;

        public void SetPan(SoundHandle sound, float pan)
        {
        }

        public void SetPitch(SoundHandle sound, float pitch)
        {
        }

        public void PlayMusic(MusicTrack track, bool loop = true, TimeSpan fade = default, float volume = 1) => CurrentMusic = track;

        public void StopMusic(TimeSpan fade = default) => CurrentMusic = null;

        public void Update(float deltaSeconds)
        {
        }

        public void Dispose()
        {
        }
    }
}
