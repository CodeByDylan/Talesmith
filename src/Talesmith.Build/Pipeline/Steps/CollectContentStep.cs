using Talesmith.Assets;
using Talesmith.Build.Content;
using Talesmith.Runtime.Hosting;
using Talesmith.Runtime.Scenes;
using Talesmith.Scripting.Compiler;

namespace Talesmith.Build.Pipeline.Steps;

/// <summary>Finds the assets the game needs, starting from its scenes, code and always-included assets.</summary>
internal sealed class CollectContentStep : IBuildStep
{
    public string Title => "Collecting content";

    public async Task ExecuteAsync(BuildContext context, CancellationToken cancellationToken)
    {
        var roots = await Task.Run(() => Roots(context, cancellationToken), cancellationToken).ConfigureAwait(false);
        var collector = new ContentCollector(context.Assets, new ContentRules(context.Game.PluginsFolder));
        var manifest = collector.Collect(roots);
        context.Content = manifest;

        foreach (var missing in manifest.MissingReferences)
        {
            context.Write(new BuildLogEntry(DateTimeOffset.Now, BuildLogLevel.Error, $"{missing.FromPath} refers to {missing.Reference}, which does not exist")
            {
                File = missing.FromPath
            });
        }

        foreach (var root in manifest.UnresolvedRoots.Where(r => r.Reason == ContentReason.AlwaysInclude))
            context.Log(BuildLogLevel.Warning, $"{root.Path} is always included but does not exist");

        var byReason = manifest.Items.GroupBy(i => i.Reason).OrderBy(g => g.Key).Select(g => $"{g.Count()} {Describe(g.Key)}");
        context.Log(BuildLogLevel.Info, $"Shipping {manifest.Items.Count:N0} assets ({string.Join(", ", byReason)}), {manifest.TotalSize / 1024.0 / 1024.0:N1} MB before packing");
    }

    internal static IEnumerable<ContentRoot> Roots(BuildContext context, CancellationToken cancellationToken)
    {
        var assets = context.Assets;
        var roots = new List<ContentRoot>();
        var start = context.Game.StartScene;
        if (start.Name == DocumentScene.SceneName)
        {
            if (start.Get(DocumentScene.PathParameter) is { } path)
                roots.Add(ContentRoot.ForPath(path, ContentReason.StartScene, GameSettings.FileName));
            else if (AssetGuid.TryParse(start.Get(DocumentScene.GuidParameter), null, out var guid))
                roots.Add(ContentRoot.ForGuid(guid, ContentReason.StartScene, GameSettings.FileName));
        }

        foreach (var value in start.Parameters?.Values ?? [])
        {
            foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!part.Contains(' ', StringComparison.Ordinal) && assets.TryGetAsset(part, out var record) && !record.IsFolder)
                    roots.Add(ContentRoot.ForPath(record.Path, ContentReason.StartScene, GameSettings.FileName));
            }
        }

        if (context.Game.LoadingScreen.Image is { } loadingImage)
            roots.Add(ContentRoot.ForPath(loadingImage, ContentReason.LoadingScreen, GameSettings.FileName));

        roots.AddRange(context.Settings.Scenes.Where(s => s.Enabled).Select(s => ContentRoot.ForPath(s.Path, ContentReason.BuildScene, BuildSettings.FileName)));

        foreach (var entry in context.Settings.AlwaysInclude.Where(e => !string.IsNullOrWhiteSpace(e)))
        {
            if (entry.StartsWith(BuildSettings.LabelPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var label = entry[BuildSettings.LabelPrefix.Length..].Trim();
                roots.AddRange(assets.Assets
                    .Where(a => a.Meta.Labels.Contains(label, StringComparer.OrdinalIgnoreCase))
                    .Select(a => ContentRoot.ForPath(a.Path, ContentReason.AlwaysInclude, entry)));
            }
            else
            {
                roots.Add(ContentRoot.ForPath(entry, ContentReason.AlwaysInclude, BuildSettings.FileName));
            }
        }

        roots.AddRange(assets.Assets.Where(a => a.Kind == AssetKind.Localization).Select(a => ContentRoot.ForPath(a.Path, ContentReason.Localization, "localization")));

        using (var compiler = new ScriptCompiler(CompileScriptsStep.ScriptOptions(context)))
            roots.AddRange(CodeReferences.Resolve(CodeReferences.FromSources(compiler.FindSources(), cancellationToken), assets, "scripts"));
        foreach (var plugin in context.Plugins.Where(p => p.Manifest is not null))
        {
            var assembly = Path.Combine(plugin.Directory, plugin.Manifest!.AssemblyFile);
            if (!File.Exists(assembly))
                continue;
            try
            {
                roots.AddRange(CodeReferences.Resolve(CodeReferences.FromAssembly(assembly), assets, plugin.Manifest.Id));
            }
            catch (BadImageFormatException ex)
            {
                context.Log(BuildLogLevel.Warning, $"The plugin {plugin.DisplayName}'s assembly could not be read for asset paths: {ex.Message}");
            }
        }

        return roots;
    }

    private static string Describe(ContentReason reason) => reason switch
    {
        ContentReason.StartScene => "from the start scene",
        ContentReason.BuildScene => "from build scenes",
        ContentReason.Dependency => "dependencies",
        ContentReason.CodeReference => "named in code",
        ContentReason.AlwaysInclude => "always included",
        ContentReason.LoadingScreen => "for the loading screen",
        _ => "string tables"
    };
}
