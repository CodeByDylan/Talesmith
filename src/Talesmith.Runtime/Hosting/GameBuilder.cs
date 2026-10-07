using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Talesmith.Assets;
using Talesmith.Assets.Hexy;
using Talesmith.Audio;
using Talesmith.Diagnostics;
using Talesmith.Ecs;
using Talesmith.Events;
using Talesmith.Input;
using Talesmith.Rendering;
using Talesmith.Runtime.Audio;
using Talesmith.Runtime.Diagnostics;
using Talesmith.Runtime.Maps;
using Talesmith.Runtime.Rendering;
using Talesmith.Runtime.Scenes;
using Talesmith.Runtime.Scheduling;
using Talesmith.Runtime.Systems;
using Talesmith.Runtime.Tweens;
using Talesmith.Systems;

namespace Talesmith.Runtime.Hosting;

/// <summary>Configures a game's services, then builds the <see cref="Game"/>.</summary>
/// <remarks>
/// Hosts register their renderer, audio backend and logging, plugins add systems and scenes, and <see cref="Build"/> fills in engine
/// defaults for everything not registered yet: a <see cref="NullRenderer"/>, silent audio, the asset manager with texture, .hexy,
/// .tscene and .tprefab importers, input, events, scenes, the built-in components and the built-in systems.
/// </remarks>
public sealed class GameBuilder
{
    private GameBuilder(string assetRoot, GameSettings settings)
    {
        AssetRoot = Path.GetFullPath(assetRoot);
        Settings = settings;
    }

    /// <summary>The folder containing the game's assets.</summary>
    public string AssetRoot { get; }

    public GameSettings Settings { get; }

    public IServiceCollection Services { get; } = new ServiceCollection();

    /// <summary>Creates a builder for the game whose assets are in <paramref name="assetRoot"/>, reading its <see cref="GameSettings"/>.</summary>
    public static GameBuilder Create(string assetRoot, GameSettings? settings = null) =>
        new(assetRoot, settings ?? GameSettings.Load(assetRoot));

    /// <summary>Uses <paramref name="renderer"/> for textures; the host draws frames with it.</summary>
    public GameBuilder UseRenderer(IRenderer renderer)
    {
        Services.Replace(ServiceDescriptor.Singleton(renderer));
        return this;
    }

    public Game Build()
    {
        AddEngineServices(Services);
        var provider = Services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        return provider.GetRequiredService<Game>();
    }

    private void AddEngineServices(IServiceCollection services)
    {
        services.AddLogging();
        services.TryAddSingleton(Settings);
        services.TryAddSingleton<IRenderer, NullRenderer>();
        services.TryAddSingleton<IEventBus, EventBus>();
        services.TryAddSingleton<EngineProfilers>();
        services.TryAddSingleton(provider => new EngineMetrics(provider.GetRequiredService<EngineProfilers>().All));
        services.TryAddSingleton<DebugOptions>();
        services.TryAddSingleton(new Viewport(Settings.View));
        services.TryAddSingleton<FramePacing>();
        services.TryAddSingleton<GameLifetime>();
        services.TryAddSingleton<IGameUi, ImmediateGameUi>();
        services.TryAddSingleton<FrameExchange>();
        services.TryAddSingleton<TextureCache>();
        services.TryAddSingleton<RenderContext>();
        services.TryAddSingleton<GameSynchronizationContext>();
        services.TryAddSingleton<GameScheduler>();
        services.TryAddSingleton<IGameScheduler>(sp => sp.GetRequiredService<GameScheduler>());
        services.TryAddSingleton<TweenService>();
        services.TryAddSingleton<ITweenService>(sp => sp.GetRequiredService<TweenService>());
        services.TryAddSingleton<SceneManager>();
        services.TryAddSingleton<ISceneManager>(sp => sp.GetRequiredService<SceneManager>());
        services.TryAddSingleton<MapSpawner>();
        services.TryAddSingleton<PlayerControl>();
        services.TryAddSingleton<Game>();
        services.TryAddSingleton<WorldEventPublisher>();
        services.TryAddScoped(sp => new World { Observer = sp.GetRequiredService<WorldEventPublisher>() });

        services.AddTalesmithInput();
        services.AddTalesmithAssets(AssetRoot);
        services.AddHexyMaps();
        services.AddTalesmithAudio();
        services.AddTalesmithScenes();
        services.AddAudioSources();

        services.AddScene<MapScene>(MapScene.SceneName);
        services.AddSystem<TransformHierarchySystem>();
        services.AddSystem<CameraSystem>();
        services.AddSystem<SpriteAnimatorSystem>();
        services.AddSystem<SpriteAnimationSystem>();
        services.AddSystem<TriggerSystem>();
        services.AddSystem<ChunkStreamingSystem>();
        services.AddSystem<TileMapRenderSystem>();
        services.AddSystem<SpriteRenderSystem>();
        services.AddSystem<DebugDrawSystem>();
    }
}
