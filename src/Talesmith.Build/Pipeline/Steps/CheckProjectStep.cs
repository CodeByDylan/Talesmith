using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Talesmith.Assets;
using Talesmith.Assets.Database;
using Talesmith.Assets.Database.Dependencies;
using Talesmith.Plugins;
using Talesmith.Runtime.Hosting;
using Talesmith.Runtime.Scenes;
using Talesmith.VFX;

namespace Talesmith.Build.Pipeline.Steps;

/// <summary>Scans the assets when the request brings no database, and checks the start scene, the build's scenes and the plugins.</summary>
internal sealed class CheckProjectStep : IBuildStep
{
    public string Title => "Checking the project";

    public async Task ExecuteAsync(BuildContext context, CancellationToken cancellationToken)
    {
        context.Assets = context.Request.Assets ?? await ScanAsync(context, cancellationToken).ConfigureAwait(false);
        CheckStartScene(context);
        foreach (var scene in context.Settings.Scenes.Where(s => s.Enabled))
        {
            if (!context.Assets.TryGetAsset(scene.Path, out var record) || record.Kind != AssetKind.Scene)
                context.Write(new BuildLogEntry(DateTimeOffset.Now, BuildLogLevel.Error, $"The scene {scene.Path} in the build settings does not exist") { File = BuildSettings.FileName });
        }

        CheckLoadingScreen(context);
        CheckPlugins(context);
        context.Icon = await ReadIconAsync(context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A missing loading screen image is only a warning: the game then shows its title.</summary>
    private static void CheckLoadingScreen(BuildContext context)
    {
        if (context.Game.LoadingScreen.Image is not { } image || context.Assets.TryGetAsset(image, out var record) && record.Kind == AssetKind.Texture)
            return;
        context.Write(new BuildLogEntry(DateTimeOffset.Now, BuildLogLevel.Warning,
            $"The loading screen's image {image} is not an image in the asset folder, so the game shows its title instead")
        {
            File = GameSettings.FileName
        });
    }

    private static async Task<byte[]?> ReadIconAsync(BuildContext context, CancellationToken cancellationToken)
    {
        if (context.Settings.Icon is not { Length: > 0 } icon)
            return null;
        var path = Path.Combine(context.AssetRoot, icon);
        if (!File.Exists(path) || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            context.Write(new BuildLogEntry(DateTimeOffset.Now, BuildLogLevel.Warning, $"The icon {icon} is not a PNG file in the asset folder, so the build has no icon")
            {
                File = BuildSettings.FileName
            });
            return null;
        }

        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<AssetDatabase> ScanAsync(BuildContext context, CancellationToken cancellationToken)
    {
        context.Progress(null, "Scanning assets");
        var builder = GameBuilder.Create(context.AssetRoot, context.Game);
        builder.Services.AddTalesmithParticles();
        await using var game = builder.Build();
        var services = game.Services;
        var database = new AssetDatabase(
            AssetDatabaseOptions.ForProject(context.Request.ProjectFolder) with { PluginsFolder = context.Game.PluginsFolder },
            services.GetServices<IAssetImporter>(),
            NullLogger<AssetDatabase>.Instance,
            DefaultDependencyExtractors.Create().Concat(services.GetServices<IAssetDependencyExtractor>()));
        var progress = new Progress<AssetScanProgress>(p => context.Progress(p.Fraction, p.CurrentPath));
        var result = await database.ScanAsync(progress, cancellationToken).ConfigureAwait(false);
        context.Log(BuildLogLevel.Info, $"Scanned {result.AssetCount:N0} assets");
        foreach (var repair in result.Repairs)
            context.Log(BuildLogLevel.Warning, $"{repair.RepairedPath} had the same guid as {repair.KeptPath} and got a new one");
        foreach (var invalid in result.InvalidMetas)
            context.Log(BuildLogLevel.Warning, $"{invalid.Path}.meta could not be read and was replaced: {invalid.Error}");
        return database;
    }

    private static void CheckStartScene(BuildContext context)
    {
        var start = context.Game.StartScene;
        var file = new BuildLogEntry(DateTimeOffset.Now, BuildLogLevel.Error, "") { File = GameSettings.FileName };
        if (start.Name == DocumentScene.SceneName)
        {
            var path = start.Get(DocumentScene.PathParameter);
            var found = path is not null
                ? context.Assets.TryGetAsset(path, out _)
                : AssetGuid.TryParse(start.Get(DocumentScene.GuidParameter), null, out var guid) && context.Assets.TryGetAsset(guid, out _);
            if (!found)
                context.Write(file with { Message = $"The start scene {path ?? start.Get(DocumentScene.GuidParameter) ?? "(none)"} does not exist" });
            return;
        }

        foreach (var (name, value) in start.Parameters ?? new Dictionary<string, string>())
        {
            foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var extension = Path.GetExtension(part);
                if (extension.Length > 1 && !part.Contains(' ', StringComparison.Ordinal) && context.Assets.Kinds.Classify(part) != AssetKind.Other
                    && !context.Assets.TryGetAsset(part, out _))
                    context.Write(file with { Message = $"The start scene's {name} {part} does not exist" });
            }
        }
    }

    private static void CheckPlugins(BuildContext context)
    {
        var options = new PluginLoadOptions
        {
            PluginsDirectory = Path.Combine(context.AssetRoot, context.Game.PluginsFolder),
            ConfigurationFile = Path.Combine(context.AssetRoot, "config", "plugins.json")
        };
        PluginLoadReport scan;
        try
        {
            using var manager = new PluginManager(options, NullLogger.Instance);
            scan = manager.Scan;
        }
        catch (InvalidDataException ex)
        {
            context.Write(new BuildLogEntry(DateTimeOffset.Now, BuildLogLevel.Error, ex.Message) { File = "config/plugins.json" });
            return;
        }

        context.Plugins = [.. scan.Plugins.Where(p => p.State is PluginState.Pending or PluginState.Loaded)];
        foreach (var plugin in scan.Plugins.Where(p => p.State is PluginState.Failed or PluginState.Skipped))
            context.Log(BuildLogLevel.Warning, $"The plugin {plugin.DisplayName} does not ship: {plugin.Reason}");
        foreach (var plugin in context.Plugins)
            context.Log(BuildLogLevel.Info, $"Shipping the plugin {plugin.DisplayName} {plugin.Manifest?.Version}");
    }
}
