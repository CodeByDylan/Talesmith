using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Talesmith.Editor.Hub;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Projects.Templates;
using Talesmith.Editor.Settings;
using Talesmith.UI.Controls;
using Talesmith.UI.Services;
using Talesmith.UI.Theming;

namespace Talesmith.Editor.Hosting;

/// <summary>Registers the services that live as long as the application: settings, theme, templates, the hub and the editor host.</summary>
public static class EditorApplicationServices
{
    /// <summary>Adds the application's services; projects get their own services from <see cref="EditorHost"/>.</summary>
    /// <param name="settings">The settings store, such as a <see cref="JsonSettingsService"/>.</param>
    /// <param name="sessions">Creates game sessions; null uses <see cref="WindowGameSessionFactory"/>.</param>
    public static IServiceCollection AddTalesmithEditorApplication(this IServiceCollection services, ISettingsService settings, IGameSessionFactory? sessions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddLogging();
        services.TryAddSingleton(settings);
        services.TryAddSingleton<IThemeManager>(_ => new ThemeManager(Application.Current ?? throw new InvalidOperationException("The Avalonia application must be running.")));
        services.TryAddSingleton(sessions ?? new WindowGameSessionFactory());
        services.AddSingleton<IProjectTemplate, EmptyTemplate>();
        services.AddSingleton<IProjectTemplate, HexAdventureTemplate>();
        services.AddSingleton<IProjectTemplate, PlatformerTemplate>();
        services.TryAddSingleton<ProjectCreator>();
        services.TryAddSingleton<EditorHost>();
        services.TryAddSingleton<IEditorHost>(sp => sp.GetRequiredService<EditorHost>());
        services.TryAddSingleton<WindowHost>();
        services.TryAddSingleton<IToastService, WindowToastService>();
        services.TryAddSingleton<IDialogService, DialogService>();
        services.TryAddSingleton<IFileDialogService, FileDialogService>();
        services.TryAddSingleton<HubViewModel>();
        services.TryAddTransient<HubWindow>();
        return services;
    }
}
