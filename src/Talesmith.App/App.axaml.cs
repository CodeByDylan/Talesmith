using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Editor.Hosting;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Projects.Templates;
using Talesmith.Editor.Settings;
using Talesmith.UI.Controls;
using Talesmith.UI.Theming;

namespace Talesmith.App;

/// <summary>The editor application: composes the services, applies the saved theme, and opens the project from the command line or the hub.</summary>
public sealed class App : Application
{
    private ServiceProvider? _services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _services = new ServiceCollection()
                .AddTalesmithEditorApplication(new JsonSettingsService(JsonSettingsService.DefaultPath))
                .BuildServiceProvider();
            ApplyTheme(_services);
            desktop.Exit += (_, _) => _services.Dispose();
            var splash = new SplashWindow();
            splash.Show();
            _ = StartAsync(desktop, _services.GetRequiredService<EditorHost>(), desktop.Args ?? [], splash);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Opens the first window behind the splash, then lets the splash go once that window is on screen.</summary>
    private async Task StartAsync(IClassicDesktopStyleApplicationLifetime desktop, EditorHost host, string[] args, SplashWindow splash)
    {
        try
        {
            await splash.WhenDrawnAsync();
            await OpenFirstWindowAsync(host, args, splash);
        }
        finally
        {
            splash.CloseAfterFirstFrameOf(desktop.MainWindow);
        }
    }

    private async Task OpenFirstWindowAsync(EditorHost host, string[] args, SplashWindow splash)
    {
        if (args is ["--new", var templateId, var parent, var name, ..])
        {
            var creator = _services!.GetRequiredService<ProjectCreator>();
            if (creator.Templates.FirstOrDefault(t => string.Equals(t.Id, templateId, StringComparison.OrdinalIgnoreCase)) is not { } template)
            {
                host.ShowHub();
                _services!.GetRequiredService<IToastService>().Show("Unknown template", $"Choose one of {string.Join(", ", creator.Templates.Select(t => t.Id))}.",
                    ToastKind.Warning);
                return;
            }

            splash.Status = $"Creating {name}…";
            args = [await creator.CreateAsync(template, Path.GetFullPath(parent), name)];
        }

        if (args is [var path, ..] && EditorProject.TryResolve(path, out var project))
        {
            splash.Status = $"Opening {new EditorProject(project).Name}…";
            try
            {
                await host.OpenProjectAsync(project);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                host.ShowHub();
                _services!.GetRequiredService<IToastService>().Show("Could not open the project", ex.Message, ToastKind.Error);
                return;
            }
        }

        host.ShowHub();
        if (args is [var missing, ..])
            _services!.GetRequiredService<IToastService>().Show("Not a Talesmith project", $"{missing} has no assets/config/game.json.", ToastKind.Warning);
    }

    private static void ApplyTheme(IServiceProvider services)
    {
        var settings = services.GetRequiredService<ISettingsService>().Current;
        var theme = services.GetRequiredService<IThemeManager>();
        theme.Mode = settings.Theme;
        if (Color.TryParse(settings.AccentColor, out var accent))
            theme.Accent = accent;
    }
}
