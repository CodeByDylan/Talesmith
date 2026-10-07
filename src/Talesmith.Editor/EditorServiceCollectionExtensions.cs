using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Talesmith.Editor.Assets;
using Talesmith.Editor.Build;
using Talesmith.Editor.CommandPalette;
using Talesmith.Editor.Commands;
using Talesmith.Editor.Console;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Hierarchy;
using Talesmith.Editor.Hosting;
using Talesmith.Editor.Inspector;
using Talesmith.Editor.Lighting;
using Talesmith.Editor.Panels;
using Talesmith.Editor.Particles;
using Talesmith.Editor.PlayMode;
using Talesmith.Editor.Plugins;
using Talesmith.Editor.Prefabs;
using Talesmith.Editor.Projects;
using Talesmith.Editor.ProjectSettings;
using Talesmith.Editor.Scripting;
using Talesmith.Editor.Selection;
using Talesmith.Editor.Shell;
using Talesmith.Editor.TileMaps;
using Talesmith.Editor.Undo;
using Talesmith.Editor.Viewport;
using Talesmith.Editor.Viewport.Gizmos;
using Talesmith.Editor.Viewport.Tools;
using Talesmith.UI;
using Talesmith.UI.Controls;
using Talesmith.UI.Services;

namespace Talesmith.Editor;

/// <summary>Registers the editor's services. This is the one place where features are wired up: each feature folder has an <c>Add…</c>
/// extension method, called here in order.</summary>
public static class EditorServiceCollectionExtensions
{
    /// <summary>Adds everything the editor needs while a project is open. The host adds the project, its plugin manager and plugin guard, the
    /// console, the settings, the theme manager, the game session factory and itself before calling this.</summary>
    public static IServiceCollection AddTalesmithEditor(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddEditorWindowServices();
        services.AddEditorCore();
        services.AddEditorConsole();
        services.AddEditorViewport();
        services.AddEditorPlayMode();
        services.AddEditorPrefabs();
        services.AddEditorHierarchy();
        services.AddEditorInspector();
        services.AddEditorGizmos();
        services.AddEditorCommandPalette();
        services.AddEditorParticles();
        services.AddEditorLighting();
        services.AddEditorAssets();
        services.AddEditorTileMaps();
        services.AddEditorScripting();
        services.AddEditorPlugins();
        services.AddEditorBuild();
        services.AddEditorProjectSettings();
        services.AddEditorShell();
        return services;
    }

    /// <summary>Dialogs, file pickers and notifications bound to the editor window.</summary>
    public static IServiceCollection AddEditorWindowServices(this IServiceCollection services)
    {
        services.TryAddSingleton<WindowHost>();
        services.TryAddSingleton<IToastService, WindowToastService>();
        services.TryAddSingleton<IDialogService, DialogService>();
        services.TryAddSingleton<IFileDialogService, FileDialogService>();
        return services;
    }

    /// <summary>The project, editor state, undo, selection, scene documents and commands.</summary>
    public static IServiceCollection AddEditorCore(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ProjectService>();
        services.TryAddSingleton<IProjectService>(sp => sp.GetRequiredService<ProjectService>());
        services.TryAddSingleton<ProjectState>();
        services.TryAddSingleton<IUndoService, UndoService>();
        services.TryAddSingleton<ISelectionService, SelectionService>();
        services.TryAddSingleton<SceneDocumentService>();
        services.TryAddSingleton<ISceneDocumentService>(sp => sp.GetRequiredService<SceneDocumentService>());
        services.AddSingleton<ICloseGuard>(sp => sp.GetRequiredService<SceneDocumentService>());
        services.TryAddSingleton<EditorCommandRegistry>();
        services.AddEditorCommands<SceneEditCommands>();
        services.TryAddSingleton<PanelRegistry>();
        services.TryAddSingleton<LayoutService>();
        services.AddEditorPanel<HistoryPanel>(new EditorPanelInfo(PanelIds.History, "History", Icons.Undo, DockLocation.Right) { Order = 20, Shortcut = "Ctrl+Shift+H" });
        return services;
    }

    /// <summary>The Console panel and navigation to what entries refer to; the <see cref="IConsole"/> itself comes from the host.</summary>
    public static IServiceCollection AddEditorConsole(this IServiceCollection services)
    {
        services.TryAddSingleton<ConsoleNavigator>();
        services.TryAddSingleton<ConsoleViewModel>();
        services.AddEditorPanel<ConsolePanel>(new EditorPanelInfo(PanelIds.Console, "Console", Icons.Terminal, DockLocation.Bottom) { Order = 10, Shortcut = "Ctrl+Shift+C" });
        return services;
    }

    /// <summary>The Scene panel with the edit world, camera, overlay, picking, icons and the built-in tools.</summary>
    public static IServiceCollection AddEditorViewport(this IServiceCollection services)
    {
        services.TryAddSingleton<EditWorld>();
        services.TryAddSingleton<IEditWorld>(sp => sp.GetRequiredService<EditWorld>());
        services.TryAddSingleton<ViewportCamera>();
        services.TryAddSingleton<ViewportOptions>();
        services.TryAddSingleton<EntityPicker>();
        services.AddEntityIconProvider<ComponentIconProvider>();
        services.TryAddSingleton<ViewportToolContext>();
        services.TryAddSingleton<ToolManager>();
        services.AddSingleton<IEditorCommandContributor>(sp => sp.GetRequiredService<ToolManager>());
        services.TryAddSingleton<ViewportService>();
        services.AddSingleton<IEditorCommandContributor>(sp => sp.GetRequiredService<ViewportService>());
        services.TryAddSingleton<ViewportGrid>();
        services.AddSingleton<IEditorCommandContributor>(sp => sp.GetRequiredService<ViewportGrid>());
        services.AddViewportTool<SelectTool>();
        services.AddViewportTool<HandTool>();
        services.AddEditorPanel<SceneViewportPanel>(new EditorPanelInfo(PanelIds.Scene, "Scene", Icons.Clapperboard, DockLocation.Center) { CanClose = false });
        return services;
    }

    /// <summary>Play, pause, step and stop, and the Game panel.</summary>
    public static IServiceCollection AddEditorPlayMode(this IServiceCollection services)
    {
        services.TryAddSingleton<PlayModeService>();
        services.TryAddSingleton<IPlayModeService>(sp => sp.GetRequiredService<PlayModeService>());
        services.TryAddSingleton<GameWindowPreview>();
        services.AddSingleton<IEditorCommandContributor>(sp => sp.GetRequiredService<PlayModeService>());
        services.AddSingleton<IEditorCommandContributor>(sp => sp.GetRequiredService<GameWindowPreview>());
        services.AddSingleton<ICloseGuard>(sp => sp.GetRequiredService<PlayModeService>());
        services.AddEditorPanel<GamePanel>(new EditorPanelInfo(PanelIds.Game, "Game", Icons.Monitor, DockLocation.Center) { Order = 1 });
        return services;
    }

    /// <summary>The command palette's entity and asset results.</summary>
    public static IServiceCollection AddEditorCommandPalette(this IServiceCollection services)
    {
        services.AddCommandPaletteProvider<EntityPaletteProvider>();
        services.AddCommandPaletteProvider<AssetPaletteProvider>();
        return services;
    }

    /// <summary>Script compilation, diagnostics, hot reload, the code editor and the Create Script command.</summary>
    public static IServiceCollection AddEditorScripting(this IServiceCollection services)
    {
        services.TryAddSingleton<ICodeEditor, ExternalCodeEditor>();
        services.TryAddSingleton<ScriptService>();
        services.TryAddSingleton<IScriptService>(sp => sp.GetRequiredService<ScriptService>());
        services.TryAddSingleton<ScriptHotReloader>();
        services.TryAddSingleton<EditSessionScripts>();
        services.AddSingleton<IPlaySessionContributor, ScriptPlayContributor>();
        services.AddEditorCommands<ScriptCommands>();
        return services;
    }

    /// <summary>The Plugins panel and plugin project scaffolding.</summary>
    public static IServiceCollection AddEditorPlugins(this IServiceCollection services)
    {
        services.TryAddSingleton<PluginScaffold>();
        services.AddEditorPanel<PluginsPanel>(new EditorPanelInfo(PanelIds.Plugins, "Plugins", Icons.Plug, DockLocation.Right) { Order = 30, OpenByDefault = false });
        return services;
    }

    /// <summary>The Build dialog, its commands and the build service.</summary>
    public static IServiceCollection AddEditorBuild(this IServiceCollection services)
    {
        services.TryAddSingleton<BuildService>();
        services.TryAddSingleton<IBuildService>(sp => sp.GetRequiredService<BuildService>());
        services.AddEditorCommands<BuildCommands>();
        return services;
    }

    /// <summary>The Project Settings window: game settings, input actions and physics layers.</summary>
    public static IServiceCollection AddEditorProjectSettings(this IServiceCollection services)
    {
        services.AddEditorCommands<ProjectSettingsCommands>();
        return services;
    }

    /// <summary>The editor window, its shell and status bar.</summary>
    public static IServiceCollection AddEditorShell(this IServiceCollection services)
    {
        services.TryAddSingleton<StatusBarViewModel>();
        services.TryAddSingleton<ProjectLoadingViewModel>();
        services.TryAddSingleton<ShellViewModel>();
        services.TryAddSingleton<EditorWindow>();
        return services;
    }
}
