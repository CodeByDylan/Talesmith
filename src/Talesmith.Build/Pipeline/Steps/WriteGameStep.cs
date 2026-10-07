using Talesmith.Assets;
using Talesmith.Assets.Packs;
using Talesmith.Build.Content;
using Talesmith.Scripting;

namespace Talesmith.Build.Pipeline.Steps;

/// <summary>Writes the game folder: the content pack with the asset index, the loose configuration, the compiled scripts and the plugins.</summary>
internal sealed class WriteGameStep : IBuildStep
{
    public const string ConfigurationCategory = "Configuration";
    public const string ScriptsCategory = "Scripts";
    public const string PluginsCategory = "Plugins";

    public string Title => "Packing content";

    public async Task ExecuteAsync(BuildContext context, CancellationToken cancellationToken)
    {
        if (Directory.Exists(context.StagingDirectory))
            Directory.Delete(context.StagingDirectory, recursive: true);
        var game = context.StagingGameDirectory;
        Directory.CreateDirectory(game);

        await WritePackAsync(context, game, cancellationToken).ConfigureAwait(false);
        CopyFolder(context, Path.Combine(context.AssetRoot, ContentRules.ConfigFolder), Path.Combine(game, ContentRules.ConfigFolder), ConfigurationCategory, _ => true);
        WriteScripts(context, game);
        foreach (var plugin in context.Plugins)
        {
            var relative = Path.GetRelativePath(Path.Combine(context.AssetRoot, context.Game.PluginsFolder), plugin.Directory);
            var editor = plugin.Manifest?.EditorAssemblyFile is { } file ? Path.GetFileNameWithoutExtension(file) : null;
            CopyFolder(context, plugin.Directory, Path.Combine(game, context.Game.PluginsFolder, relative), PluginsCategory, path =>
                (editor is null || !string.Equals(Path.GetFileNameWithoutExtension(path).Replace(".deps", "", StringComparison.Ordinal), editor, StringComparison.OrdinalIgnoreCase))
                && !path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                && (context.Profile.DebugSymbols || !path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)));
        }
    }

    private static async Task WritePackAsync(BuildContext context, string game, CancellationToken cancellationToken)
    {
        var manifest = context.Content ?? throw new InvalidOperationException("Content must be collected before it is packed.");
        var index = new AssetIndex(manifest.Items.Select(i => new AssetCatalogEntry(i.Path, i.Asset.Meta))
            .Concat(manifest.Folders.Select(f => new AssetCatalogEntry(f.Path, f.Meta))));
        await using var writer = ContentPackWriter.Create(Path.Combine(game, ContentPack.FileName));
        await writer.AddBytesAsync(AssetIndex.FileName, index.ToUtf8Bytes(), PackCompression.Brotli, cancellationToken).ConfigureAwait(false);
        var done = 0;
        foreach (var item in manifest.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.Progress((double)done++ / Math.Max(1, manifest.Items.Count), item.Path);
            var file = Path.Combine(context.AssetRoot, item.Path);
            var entry = await writer.AddFileAsync(item.Path, file, ContentCompression.For(item.Path, context.Settings.Compress), cancellationToken).ConfigureAwait(false);
            context.AddSize(item.Asset.Kind.DisplayName, entry.StoredLength, entry.Length);
            lock (context.AssetSizes)
                context.AssetSizes.Add(new BuildAssetSize(item.Path, item.Asset.Kind.DisplayName, entry.Length, entry.StoredLength, item.Reason.ToString()));
        }

        await writer.FinishAsync(cancellationToken).ConfigureAwait(false);
        var packed = writer.Entries.Sum(e => e.StoredLength);
        var original = writer.Entries.Sum(e => e.Length);
        context.Log(BuildLogLevel.Info, $"Packed {writer.Entries.Count - 1:N0} assets into {ContentPack.FileName}: {original / 1048576.0:N1} MB, {packed / 1048576.0:N1} MB stored");
    }

    private static void WriteScripts(BuildContext context, string game)
    {
        if (context.Scripts?.Image is not { } image)
            return;
        var path = Path.Combine(game, ScriptingOptions.DefaultAssemblyPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, image);
        context.AddSize(ScriptsCategory, image.Length, image.Length);
        if (context.Profile.DebugSymbols && context.Scripts.Symbols is { } symbols)
        {
            File.WriteAllBytes(Path.ChangeExtension(path, ".pdb"), symbols);
            context.AddSize(ScriptsCategory, symbols.Length, symbols.Length);
        }
    }

    private static void CopyFolder(BuildContext context, string source, string target, string category, Func<string, bool> include)
    {
        if (!Directory.Exists(source))
            return;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (relative.Split(Path.DirectorySeparatorChar).Any(part => part.StartsWith('.')) || AssetMetaFile.IsMetaPath(relative.Replace('\\', '/'))
                                                                                             || !include(relative))
                continue;
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
            var size = new FileInfo(destination).Length;
            context.AddSize(category, size, size);
        }
    }
}
