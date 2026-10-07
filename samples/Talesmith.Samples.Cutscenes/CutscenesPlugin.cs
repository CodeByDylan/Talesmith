using Microsoft.Extensions.DependencyInjection;
using Talesmith.Assets;
using Talesmith.Avalonia.Overlays;
using Talesmith.Plugins;
using Talesmith.Runtime.Scenes;
using Talesmith.Systems;

namespace Talesmith.Samples.Cutscenes;

/// <summary>Registers cutscenes, dialogue and the dialogue overlay with the engine.</summary>
public sealed class CutscenesPlugin : IPlugin
{
    public void Configure(IPluginBuilder builder)
    {
        var services = builder.Services;
        services.AddSingleton<IAssetImporter, CutsceneImporter>();
        services.AddSingleton<DialogueState>();
        services.AddSingleton<CutsceneJournal>();
        services.AddSingleton<CutscenePlayer>();
        services.AddSingleton<IGameOverlay, DialogueOverlay>();
        services.AddSystem<DialogueInputSystem>();
        services.AddSceneListener<CutsceneTriggers>();
    }
}
