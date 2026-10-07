using Microsoft.Extensions.DependencyInjection;
using Talesmith.Avalonia.Overlays;
using Talesmith.Plugins;
using Talesmith.Runtime.Scenes;
using Talesmith.Samples.Shared;
using Talesmith.Systems;

namespace Talesmith.Samples.IsleHopper;

/// <summary>Registers the Isle Hopper gameplay with the engine.</summary>
public sealed class IsleHopperPlugin : IPlugin
{
    public void Configure(IPluginBuilder builder)
    {
        var services = builder.Services;
        services.AddSingleton<IsleHud>();
        services.AddScoped<Course>();
        services.AddSingleton<IGameOverlay, HudOverlay>();
        services.AddGameMenu();
        services.AddSystem<HeroSpawnSystem>();
        services.AddSystem<CourseFlowSystem>();
        services.AddSystem<HeroInputSystem>();
        services.AddSystem<HeroMovementSystem>();
        services.AddSystem<CrabSystem>();
        services.AddSystem<PickupSystem>();
        services.AddSystem<SmoothMotionStepStartSystem>();
        services.AddSystem<SmoothMotionStepEndSystem>();
        services.AddSystem<SmoothMotionSystem>();
        services.AddSystem<CameraFocusSystem>();
        services.AddSystem<ParallaxSystem>();
        services.AddSceneListener<LevelMusic>();
    }
}
