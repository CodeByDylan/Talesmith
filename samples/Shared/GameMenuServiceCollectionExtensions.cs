using Microsoft.Extensions.DependencyInjection;
using Talesmith.Avalonia.Overlays;
using Talesmith.Systems;

namespace Talesmith.Samples.Shared;

/// <summary>Registers the pause menu shared by the sample games.</summary>
public static class GameMenuServiceCollectionExtensions
{
    /// <summary>Adds the pause menu with its settings, the frame-rate counter and the system that opens the menu on the "Menu" action.</summary>
    public static IServiceCollection AddGameMenu(this IServiceCollection services)
    {
        services.AddSingleton<GameMenu>();
        services.AddSingleton<IGameOverlay, FrameRateOverlay>();
        services.AddSingleton<IGameOverlay, GameMenuOverlay>();
        services.AddSystem<GameMenuSystem>();
        return services;
    }
}
