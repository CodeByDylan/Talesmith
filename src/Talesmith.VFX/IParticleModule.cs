using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Ecs;

namespace Talesmith.VFX;

/// <summary>A particle module contributed by a plugin, such as wind zones or attraction to a target.</summary>
/// <remarks>
/// Register implementations with <see cref="ParticleModuleServiceCollectionExtensions.AddParticleModule{T}"/> under a stable type name;
/// presets and scenes save the name and the module's public fields, and the editor offers registered modules in its Add Module menu.
/// Modules run on the game thread and must not allocate per frame.
/// </remarks>
public interface IParticleModule
{
    bool Enabled { get; }

    /// <summary>Initializes particles <paramref name="first"/> to <paramref name="first"/> + <paramref name="count"/> right after they were emitted.</summary>
    void OnEmit(ParticleModuleContext context, int first, int count)
    {
    }

    /// <summary>Changes alive particles once per simulation step, after the built-in forces and before positions move.</summary>
    void Update(ParticleModuleContext context);
}

/// <summary>What a plugin module can read and change during a simulation step.</summary>
public sealed class ParticleModuleContext
{
    internal ParticleRandom RandomState;

    internal ParticleModuleContext(ParticleBuffer particles)
    {
        Particles = particles;
    }

    /// <summary>The emitter's alive particles, as spans of each attribute.</summary>
    public ParticleBuffer Particles { get; }

    /// <summary>The settings being simulated.</summary>
    public ParticleSettings Settings { get; internal set; } = null!;

    /// <summary>Seconds covered by this step, already scaled by the simulation speed.</summary>
    public float DeltaTime { get; internal set; }

    /// <summary>Seconds since the emitter started playing.</summary>
    public double Time { get; internal set; }

    /// <summary>The emitter's world position.</summary>
    public Vector2 EmitterPosition { get; internal set; }

    /// <summary>Whether particle positions are in world space or relative to the emitter.</summary>
    public ParticleSimulationSpace Space { get; internal set; }

    /// <summary>The emitter's entity, or <see cref="Entity.Null"/> when simulated outside a world.</summary>
    public Entity Emitter { get; internal set; }

    public World? World { get; internal set; }

    /// <summary>The emitter's seeded random numbers; use them to keep playback deterministic.</summary>
    public ref ParticleRandom Random => ref RandomState;
}

/// <summary>A plugin module type that presets, scenes and the editor know by a stable name.</summary>
/// <param name="TypeName">The name written to files, such as "Acme.Wind"; it must not change between versions.</param>
public sealed record ParticleModuleRegistration(string TypeName, Type Type, string DisplayName);

/// <summary>Finds plugin module types by their saved names and back.</summary>
public sealed class ParticleModuleRegistry
{
    private readonly Dictionary<string, ParticleModuleRegistration> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, ParticleModuleRegistration> _byType = [];

    public ParticleModuleRegistry(IEnumerable<ParticleModuleRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        foreach (var registration in registrations)
        {
            _byName[registration.TypeName] = registration;
            _byType[registration.Type] = registration;
        }
    }

    public static ParticleModuleRegistry Empty { get; } = new([]);

    /// <summary>Every registered module, for the editor's Add Module menu.</summary>
    public IEnumerable<ParticleModuleRegistration> All => _byName.Values;

    public bool TryGetType(string typeName, [NotNullWhen(true)] out ParticleModuleRegistration? registration) =>
        _byName.TryGetValue(typeName, out registration);

    public bool TryGetName(Type type, [NotNullWhen(true)] out string? typeName)
    {
        typeName = _byType.TryGetValue(type, out var registration) ? registration.TypeName : null;
        return typeName is not null;
    }

    /// <summary>Creates a module with its default values.</summary>
    public IParticleModule Create(string typeName) =>
        _byName.TryGetValue(typeName, out var registration)
            ? (IParticleModule)Activator.CreateInstance(registration.Type)!
            : throw new KeyNotFoundException($"No particle module is registered as '{typeName}'.");

    /// <summary>A registry naming each module type after its full type name, for copying settings outside dependency injection.</summary>
    internal static ParticleModuleRegistry ForInstances(IEnumerable<IParticleModule> modules) =>
        new(modules.Where(m => m is not UnknownParticleModule).Select(m => m.GetType()).Distinct()
            .Select(t => new ParticleModuleRegistration(t.FullName ?? t.Name, t, t.Name)));
}

/// <summary>A saved module whose type is not registered, such as one from a disabled plugin; it does nothing and is saved back unchanged.</summary>
public sealed class UnknownParticleModule(string typeName, JsonObject data) : IParticleModule
{
    public string TypeName { get; } = typeName;

    public JsonObject Data { get; } = data;

    public bool Enabled => false;

    public void Update(ParticleModuleContext context)
    {
    }
}

/// <summary>Registers plugin particle modules.</summary>
public static class ParticleModuleServiceCollectionExtensions
{
    /// <summary>Makes a module available to presets, scenes and the editor under a stable <paramref name="typeName"/>.</summary>
    public static IServiceCollection AddParticleModule<T>(this IServiceCollection services, string typeName, string? displayName = null)
        where T : class, IParticleModule, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        var registered = services.Any(d => d.ServiceType == typeof(ParticleModuleRegistration)
            && d.ImplementationInstance is ParticleModuleRegistration existing && (existing.Type == typeof(T) || existing.TypeName == typeName));
        if (!registered)
            services.AddSingleton(new ParticleModuleRegistration(typeName, typeof(T), displayName ?? typeof(T).Name));
        return services;
    }
}
