using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Microsoft.Extensions.Logging;
using Talesmith.Assets;
using Talesmith.Assets.Packs;
using Talesmith.Avalonia.Loading;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Avalonia.Hosting;

/// <summary>Runs a game in a desktop window.</summary>
public static class DesktopGame
{
    /// <summary>Starts Avalonia and shows the game until its window closes; returns the process exit code.</summary>
    public static int Run(DesktopGameOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        NativeLibraryIsolation.Apply();
        var fifo = options.VulkanCompositing && PresentMode.PreferFifo();
        var builder = AppBuilder.Configure(() => new DesktopGameApplication(options, fifo)).UsePlatformDetect();
        if (options.VulkanCompositing)
            builder = builder.With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Vulkan, X11RenderingMode.Glx, X11RenderingMode.Software] });
        // The Linux player's Skia finds fonts without fontconfig, so its default font is the bundled Inter, as in the editor, rather than
        // whichever font it finds first; games then also start on systems with no fonts in /usr/share/fonts.
        if (OperatingSystem.IsLinux())
            builder = builder.With(new FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter" });
        return builder
            .WithInterFont()
            .StartWithClassicDesktopLifetime([], ShutdownMode.OnMainWindowClose);
    }
}

/// <summary>The Avalonia application behind <see cref="DesktopGame"/>.</summary>
/// <param name="requestedFifo">Whether <see cref="PresentMode.PreferFifo"/> asked the drivers for FIFO presentation, which is logged.</param>
public sealed partial class DesktopGameApplication(DesktopGameOptions options, bool requestedFifo = false) : Application
{
    private ILoggerFactory? _loggers;

    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _loggers = LoggerFactory.Create(logging => logging
                .SetMinimumLevel(options.MinimumLogLevel)
                .AddSimpleConsole(console => console.SingleLine = true));
            desktop.Exit += (_, _) => _loggers.Dispose();
            if (requestedFifo)
            {
                var logger = _loggers.CreateLogger<DesktopGameApplication>();
                LogFifoRequested(logger, PresentMode.Variable);
            }
            _ = ShowMainWindowAsync(desktop, _loggers);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Opens the window on the game's loading screen at once, then builds the game behind it.</summary>
    private async Task ShowMainWindowAsync(IClassicDesktopStyleApplicationLifetime desktop, ILoggerFactory loggers)
    {
        var logger = loggers.CreateLogger<DesktopGameApplication>();
        GameSettings settings;
        Exception? unreadable = null;
        try
        {
            settings = GameSettings.Load(options.AssetRoot);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            settings = new GameSettings();
            unreadable = ex;
        }

        var window = new GameWindow(settings, OpenAssets(options.AssetRoot), options.DeveloperTools ? options.CaptureDirectory : null, options.Threading,
            loggers.CreateLogger<LoadingScreen>());
        desktop.MainWindow = window;
        window.Show();
        if (unreadable is not null)
        {
            Fail(unreadable);
            return;
        }

        // Anything that keeps the game from starting is shown in its window, rather than lost in this unobserved task.
        try
        {
            window.Attach(await GameSession.CreateForWindowAsync(options, loggers));
        }
        catch (Exception ex)
        {
            Fail(ex);
        }

        void Fail(Exception error)
        {
            LogStartFailed(logger, error);
            window.LoadingScreen.ShowError(error.Message);
        }
    }

    /// <summary>The game's assets, for the loading screen's image; null when the folder cannot be read, which the game then reports.</summary>
    private static IAssetSource? OpenAssets(string assetRoot)
    {
        try
        {
            return PackAssetSource.OpenFolder(assetRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AssetException)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Set {Variable}=fifo so the window's Vulkan compositor waits for each display refresh")]
    private static partial void LogFifoRequested(ILogger logger, string variable);

    [LoggerMessage(Level = LogLevel.Critical, Message = "The game could not start")]
    private static partial void LogStartFailed(ILogger logger, Exception exception);
}
