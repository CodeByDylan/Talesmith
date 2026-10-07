using Talesmith.Authoring;
using Talesmith.VFX.Presets;

namespace Talesmith.VFX;

/// <summary>Whether particles move with their emitter after they are emitted.</summary>
public enum ParticleSimulationSpace
{
    /// <summary>Particles stay where they were emitted when the emitter moves, leaving trails.</summary>
    World,

    /// <summary>Particles move, turn and scale with the emitter, like a flame attached to a torch.</summary>
    Local
}

/// <summary>What an emitter does while it and its particles are outside the camera's view.</summary>
public enum ParticleCullingMode
{
    /// <summary>Looping emitters pause; one-shot emitters keep simulating so they finish on time.</summary>
    Automatic,

    AlwaysSimulate,

    PauseWhenOffscreen
}

/// <summary>Everything that describes a particle effect: playback, then one module per aspect of the particles.</summary>
/// <remarks>Saved inline in <see cref="ParticleEmitter"/> components and as <c>.tparticles</c> presets. Edits take effect on the next frame.</remarks>
public sealed class ParticleSettings
{
    [Header("Playback")]
    [Tooltip("Seconds in one cycle of emission; bursts are timed within it.")]
    [Range(0.05, 3600)]
    public float Duration = 5;

    [Tooltip("Starts a new cycle when one ends; otherwise emission stops after one cycle.")]
    public bool Looping = true;

    [Tooltip("Starts looping effects as if a full cycle had already played, so fire is already burning when the scene appears.")]
    public bool Prewarm;

    [Label("Play on start")]
    [Tooltip("Starts playing as soon as the emitter is simulated.")]
    public bool PlayOnStart = true;

    [Label("Start delay")]
    [Tooltip("Seconds to wait after playing starts before emitting.")]
    [Range(0, 3600)]
    public float StartDelay;

    [Label("Simulation speed")]
    [Tooltip("Plays the effect faster or slower than game time.")]
    [Range(0, 10, Step = 0.05)]
    public float SimulationSpeed = 1;

    [Label("Simulation space")]
    [Tooltip("World leaves particles behind when the emitter moves; Local carries them along.")]
    public ParticleSimulationSpace SimulationSpace = ParticleSimulationSpace.World;

    [Label("Max particles")]
    [Tooltip("The most particles this emitter keeps alive; emission pauses at the limit.")]
    [Range(1, 1000000, Step = 1)]
    public int MaxParticles = 1000;

    [Tooltip("Makes the effect play the same way every time; 0 picks a new seed each time it plays.")]
    public int Seed;

    [Tooltip("What happens while the emitter is outside the camera's view.")]
    public ParticleCullingMode Culling = ParticleCullingMode.Automatic;

    [Header("Modules")]
    public EmissionModule Emission = new();

    public ShapeModule Shape = new();

    [Label("Initial values")]
    public InitialModule Initial = new();

    [Label("Velocity over lifetime")]
    public VelocityOverLifetimeModule VelocityOverLifetime = new();

    [Label("Forces and gravity")]
    public ForceModule Forces = new();

    public DragModule Drag = new();

    public NoiseModule Noise = new();

    [Label("Color over lifetime")]
    public ColorOverLifetimeModule ColorOverLifetime = new();

    [Label("Size over lifetime")]
    public SizeOverLifetimeModule SizeOverLifetime = new();

    [Label("Rotation over lifetime")]
    public RotationOverLifetimeModule RotationOverLifetime = new();

    [Label("Texture sheet animation")]
    public TextureSheetModule TextureSheet = new();

    public CollisionModule Collision = new();

    public ParticleRendererModule Renderer = new();

    [Label("Plugin modules")]
    [Tooltip("Modules added by plugins, run in order after the built-in forces.")]
    public List<IParticleModule> CustomModules = [];

    /// <summary>A deep copy, for example to edit a preset without changing it.</summary>
    /// <param name="modules">Resolves plugin module types; by default, the modules' own types are used.</param>
    public ParticleSettings Clone(ParticleModuleRegistry? modules = null) =>
        ParticlePresetSerializer.CloneSettings(this, modules ?? ParticleModuleRegistry.ForInstances(CustomModules));
}
