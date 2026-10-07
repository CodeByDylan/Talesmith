using Microsoft.Extensions.DependencyInjection;
using Talesmith.Avalonia.Overlays;
using Talesmith.Plugins;
using Talesmith.Runtime.Scenes;
using Talesmith.Samples.Shared;
using Talesmith.Systems;

namespace Talesmith.Samples.HexQuest;

/// <summary>Registers the Hex Quest gameplay with the engine.</summary>
public sealed class HexQuestPlugin : IPlugin
{
    public void Configure(IPluginBuilder builder)
    {
        var services = builder.Services;
        services.AddSingleton<QuestHud>();
        services.AddSingleton<IGameOverlay, HudOverlay>();
        services.AddGameMenu();
        services.AddSystem<HeroSpawnSystem>();
        services.AddSystem<HeroMovementSystem>();
        services.AddSystem<CameraZoomSystem>();
        services.AddSceneListener<AmbientMusic>();
    }
}
