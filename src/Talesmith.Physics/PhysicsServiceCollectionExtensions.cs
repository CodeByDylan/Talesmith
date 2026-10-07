using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Talesmith.Authoring;
using Talesmith.Physics.Systems;
using Talesmith.Runtime.Scenes;
using Talesmith.Systems;
using Talesmith.VFX;

namespace Talesmith.Physics;

/// <summary>Registers the physics module.</summary>
public static class PhysicsServiceCollectionExtensions
{
    /// <summary>Adds the physics components, a <see cref="IPhysicsWorld"/> per scene, the systems that step, interpolate and draw it, and particle collision with it.</summary>
    /// <param name="configure">Adjusts the settings every scene's world starts from, such as the layer collision matrix. Gravity comes from
    /// the scene's environment when the scene starts.</param>
    /// <remarks>Calling it again only applies <paramref name="configure"/>.</remarks>
    public static IServiceCollection AddTalesmithPhysics(this IServiceCollection services, Action<PhysicsSettings>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(PhysicsSettings))?.ImplementationInstance as PhysicsSettings;
        if (existing is not null)
        {
            configure?.Invoke(existing);
            return services;
        }

        var settings = new PhysicsSettings();
        configure?.Invoke(settings);
        services.AddSingleton(settings);
        services.TryAddSingleton<PhysicsDebugOptions>();
        services.TryAddScoped<PhysicsWorld>();
        services.TryAddScoped<IPhysicsWorld>(provider => provider.GetRequiredService<PhysicsWorld>());
        services.AddSceneListener<ScenePhysicsListener>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IParticleCollisionProvider, PhysicsParticleCollisions>());

        services.AddComponent<Collider2D>();
        services.AddComponent<Rigidbody2D>();
        services.AddComponent<CharacterController2D>();
        services.AddComponent<TileMapCollider2D>();

        services.AddSystem<PhysicsPrepareSystem>();
        services.AddSystem<PhysicsStepSystem>();
        services.AddSystem<PhysicsInterpolationSystem>();
        services.AddSystem<PhysicsDebugDrawSystem>();
        return services;
    }
}
