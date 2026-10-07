using Talesmith.Assets;
using Talesmith.Authoring;

namespace Talesmith.VFX;

/// <summary>Emits, simulates and draws particles at the entity's <see cref="Runtime.Components.Transform"/>.</summary>
/// <remarks>
/// The effect is described by <see cref="Settings"/>, or by a <c>.tparticles</c> <see cref="Preset"/> while one is set. Particles live in
/// <see cref="Simulation"/>, not as entities, so thousands of them cost no more than their arrays. Playback methods may be called at any
/// time; they take effect on the next simulation update.
/// </remarks>
[Component("Particle Emitter", Category = "Effects", Icon = "sparkles", Description = "Emits particles such as fire, smoke, sparks, rain and magic.")]
public sealed class ParticleEmitter
{
    [Tooltip("A .tparticles preset to play instead of the settings below; clear it to edit settings here.")]
    [AssetFilter(Presets.ParticlePresetSerializer.Extension)]
    public AssetGuid Preset;

    public ParticleSettings Settings = new();

    public ParticleEmitter()
    {
    }

    public ParticleEmitter(ParticleSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Settings = settings;
    }

    /// <summary>The running particles and playback state.</summary>
    [Transient]
    public ParticleSimulation Simulation { get; } = new();

    [Transient]
    public bool IsPlaying => Simulation.IsPlaying;

    /// <summary>Whether a played effect has ended and every particle has died, for one-shot effects such as explosions.</summary>
    [Transient]
    public bool IsFinished => Simulation.IsFinished;

    [Transient]
    public int AliveCount => Simulation.AliveCount;

    /// <inheritdoc cref="ParticleSimulation.Play"/>
    public void Play() => Simulation.Play();

    /// <inheritdoc cref="ParticleSimulation.Stop"/>
    public void Stop(ParticleStopBehavior behavior = ParticleStopBehavior.StopEmitting) => Simulation.Stop(behavior);

    /// <inheritdoc cref="ParticleSimulation.Pause"/>
    public void Pause() => Simulation.Pause();

    /// <inheritdoc cref="ParticleSimulation.Clear"/>
    public void Clear() => Simulation.Clear();

    /// <inheritdoc cref="ParticleSimulation.Restart"/>
    public void Restart() => Simulation.Restart();

    /// <inheritdoc cref="ParticleSimulation.Emit"/>
    public void Emit(int count) => Simulation.Emit(count);
}
