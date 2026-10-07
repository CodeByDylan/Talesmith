using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Talesmith.Assets.Database;
using Talesmith.Assets.Textures;
using Talesmith.Rendering;

namespace Talesmith.Assets.Tests;

public sealed class AssetDatabaseTests : IAsyncDisposable
{
    private readonly TempFolder _folder = new();
    private readonly ConcurrentQueue<AssetChange> _changes = new();
    private AssetDatabase? _database;

    public AssetDatabaseTests() => Directory.CreateDirectory(_folder.AssetRoot);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
        _folder.Dispose();
    }

    [Fact]
    public async Task ScanningCreatesMetaFilesForFilesAndFolders()
    {
        _folder.Write("sprites/hero.png", "png");
        _folder.Write("notes.txt", "hello");

        var result = await ScanAsync();

        Assert.Equal(3, result.MetasCreated);
        Assert.True(File.Exists(_folder.FullPath("sprites/hero.png.meta")));
        Assert.True(_folder.ReadMeta("sprites").IsFolder);
        var texture = _folder.ReadMeta("sprites/hero.png");
        Assert.Equal(TextureImporter.ImporterId, texture.Importer);
        Assert.Equal(1, texture.ImporterVersion);
        Assert.Null(_folder.ReadMeta("notes.txt").Importer);
        Assert.True(_database!.TryGetAsset("sprites/hero.png", out var record));
        Assert.Equal(AssetKind.Texture, record.Kind);
        Assert.NotNull(record.ContentHash);
        Assert.True(_database.Catalog.TryGetPath(texture.Guid, out var path));
        Assert.Equal("sprites/hero.png", path);
    }

    [Fact]
    public async Task HiddenFilesAndMetaFilesAreNotAssets()
    {
        _folder.Write(".git/config", "x");
        _folder.Write("hero.png~", "backup");
        _folder.Write("hero.png", "png");

        await ScanAsync();

        Assert.Equal(["hero.png"], _database!.Assets.Select(asset => asset.Path));
    }

    [Fact]
    public async Task ScriptBuildOutputIsNotScannedOrGivenMetaFiles()
    {
        _folder.Write("scripts/Player.cs", "class Player;");
        _folder.Write("scripts/bin/Game.Scripts.dll", "dll");
        _folder.Write("scripts/obj/project.assets.json", "{}");
        _folder.Write("scripts/Enemies/bin/Debug/Enemy.dll", "dll");
        _folder.Write("scripts/.vs/state", "x");
        _folder.Write("sprites/bin/hero.png", "png");

        await ScanAsync();

        Assert.Equal(["scripts", "scripts/Enemies", "scripts/Player.cs", "sprites", "sprites/bin", "sprites/bin/hero.png"],
            _database!.Assets.Select(asset => asset.Path).Order(StringComparer.Ordinal));
        Assert.False(File.Exists(_folder.FullPath("scripts/bin.meta")));
        Assert.False(File.Exists(_folder.FullPath("scripts/obj.meta")));
        Assert.False(File.Exists(_folder.FullPath("scripts/bin/Game.Scripts.dll.meta")));
        Assert.False(File.Exists(_folder.FullPath("scripts/Enemies/bin.meta")));
    }

    [Fact]
    public async Task WatchingIgnoresScriptBuildOutput()
    {
        await ScanAsync();
        var added = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _database!.AssetsChanged += (_, e) =>
        {
            if (e.Changes.Any(change => change.Path == "scripts/Player.cs"))
                added.TrySetResult();
        };
        _database.StartWatching();

        _folder.Write("scripts/bin/Game.Scripts.dll", "dll");
        _folder.Write("scripts/obj/Game.Scripts.pdb", "pdb");
        _folder.Write("scripts/Player.cs", "class Player;");

        await added.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        Assert.DoesNotContain(_changes, change => change.Path.Contains("bin", StringComparison.Ordinal) || change.Path.Contains("obj", StringComparison.Ordinal));
        Assert.False(File.Exists(_folder.FullPath("scripts/bin.meta")));
        Assert.False(File.Exists(_folder.FullPath("scripts/bin/Game.Scripts.dll.meta")));
    }

    [Fact]
    public async Task ThePluginsFolderIsNotScannedOrGivenMetaFiles()
    {
        _folder.Write("plugins/weather/plugin.json", "{}");
        _folder.Write("plugins/weather/Weather.dll", "dll");
        _folder.Write("plugins/weather/assets/rain.png", "png");
        _folder.Write("sprites/hero.png", "png");

        await ScanAsync();

        Assert.Equal(["sprites", "sprites/hero.png"], _database!.Assets.Select(asset => asset.Path).Order(StringComparer.Ordinal));
        Assert.False(File.Exists(_folder.FullPath("plugins.meta")));
        Assert.Empty(Directory.GetFiles(_folder.FullPath("plugins"), "*.meta", SearchOption.AllDirectories));
        Assert.Empty(_database.GetReport().OrphanedMetas);
    }

    [Fact]
    public async Task TheConfiguredPluginsFolderIsTheOneLeftOut()
    {
        _folder.Write("mods/weather/plugin.json", "{}");
        _folder.Write("plugins/notes.txt", "not a plugin folder here");
        _database = CreateDatabase(pluginsFolder: "mods");

        await ScanAsync();

        Assert.Equal(["plugins", "plugins/notes.txt"], _database.Assets.Select(asset => asset.Path).Order(StringComparer.Ordinal));
        Assert.False(File.Exists(_folder.FullPath("mods/weather/plugin.json.meta")));
    }

    [Fact]
    public async Task WatchingIgnoresThePluginsFolder()
    {
        await ScanAsync();
        var added = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _database!.AssetsChanged += (_, e) =>
        {
            if (e.Changes.Any(change => change.Path == "hero.png"))
                added.TrySetResult();
        };
        _database.StartWatching();

        _folder.Write("plugins/weather/Weather.dll", "dll");
        _folder.Write("plugins/weather/plugin.json", "{}");
        _folder.Write("hero.png", "png");

        await added.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        Assert.DoesNotContain(_changes, change => change.Path.StartsWith("plugins", StringComparison.Ordinal));
        Assert.DoesNotContain(Directory.GetFiles(_folder.AssetRoot, "*.meta", SearchOption.AllDirectories), f => f.Contains("plugins", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RescanningKeepsGuidsAndReportsNothing()
    {
        _folder.Write("a.png", "a");
        await ScanAsync();
        var guid = _folder.ReadMeta("a.png").Guid;
        _changes.Clear();

        var result = await _database!.ScanAsync(cancellationToken: Token);

        Assert.Equal(0, result.MetasCreated);
        Assert.Empty(_changes);
        Assert.Equal(guid, _folder.ReadMeta("a.png").Guid);
    }

    [Fact]
    public async Task ACopiedAssetWithItsMetaGetsANewGuid()
    {
        var guid = AssetGuid.NewGuid();
        _folder.Write("hero.png", "png");
        _folder.WriteMeta("hero.png", new AssetMeta(guid) { Importer = "texture", ImporterVersion = 1, Labels = ["hero"] });
        await ScanAsync();

        File.Copy(_folder.FullPath("hero.png"), _folder.FullPath("hero copy.png"));
        File.Copy(_folder.FullPath("hero.png.meta"), _folder.FullPath("hero copy.png.meta"));
        _changes.Clear();
        await _database!.RefreshAsync(["hero copy.png"], Token);

        var copy = _folder.ReadMeta("hero copy.png");
        Assert.NotEqual(guid, copy.Guid);
        Assert.Equal(["hero"], copy.Labels);
        Assert.Equal(guid, _folder.ReadMeta("hero.png").Guid);
        var repair = Assert.Single(_database.GetReport().DuplicateGuids);
        Assert.Equal("hero.png", repair.KeptPath);
        Assert.Equal("hero copy.png", repair.RepairedPath);
        var added = Assert.Single(_changes);
        Assert.Equal(AssetChangeKind.Added, added.Change);
        Assert.Equal(guid, added.ReplacedGuid);
    }

    [Fact]
    public async Task DuplicateGuidsFoundInOneScanKeepTheOldestFile()
    {
        var guid = AssetGuid.NewGuid();
        _folder.Write("b.png", "b");
        _folder.WriteMeta("b.png", new AssetMeta(guid));
        File.SetCreationTimeUtc(_folder.FullPath("b.png"), DateTime.UtcNow.AddDays(-1));
        _folder.Write("a.png", "a");
        _folder.WriteMeta("a.png", new AssetMeta(guid));

        await ScanAsync();

        Assert.Equal(2, _database!.Assets.Select(asset => asset.Guid).Distinct().Count());
        Assert.Single(_database.GetReport().DuplicateGuids);
    }

    [Fact]
    public async Task MovingAnAssetMovesItsMetaAndKeepsItsGuid()
    {
        _folder.Write("hero.png", "png");
        Directory.CreateDirectory(_folder.FullPath("sprites"));
        await ScanAsync();
        var guid = _folder.ReadMeta("hero.png").Guid;
        _changes.Clear();

        var record = await _database!.MoveAsync("hero.png", "sprites/hero.png", Token);

        Assert.Equal(guid, record.Guid);
        Assert.False(File.Exists(_folder.FullPath("hero.png.meta")));
        Assert.Equal(guid, _folder.ReadMeta("sprites/hero.png").Guid);
        var moved = Assert.Single(_changes);
        Assert.Equal(AssetChangeKind.Moved, moved.Change);
        Assert.Equal("hero.png", moved.OldPath);
        Assert.True(_database.Catalog.TryGetPath(guid, out var path));
        Assert.Equal("sprites/hero.png", path);
    }

    [Fact]
    public async Task RenamingAFolderMovesEverythingInsideIt()
    {
        _folder.Write("art/hero.png", "png");
        await ScanAsync();
        var guid = _folder.ReadMeta("art/hero.png").Guid;
        _changes.Clear();

        await _database!.RenameAsync("art", "sprites", Token);

        Assert.True(_database.TryGetAsset(guid, out var record));
        Assert.Equal("sprites/hero.png", record.Path);
        Assert.Contains(_changes, change => change is { Change: AssetChangeKind.Moved, Path: "sprites/hero.png", OldPath: "art/hero.png" });
        Assert.Contains(_changes, change => change is { Change: AssetChangeKind.Moved, Path: "sprites", OldPath: "art" });
    }

    [Fact]
    public async Task RenamingOnlyTheCaseWorks()
    {
        _folder.Write("hero.png", "png");
        await ScanAsync();

        var record = await _database!.RenameAsync("hero.png", "Hero.png", Token);

        Assert.Equal("Hero.png", record.Path);
        Assert.True(File.Exists(_folder.FullPath("Hero.png.meta")));
    }

    [Fact]
    public async Task MovesIntoExistingFilesAreRefused()
    {
        _folder.Write("a.png", "a");
        _folder.Write("b.png", "b");
        await ScanAsync();

        await Assert.ThrowsAsync<IOException>(() => _database!.MoveAsync("a.png", "b.png", Token));
        await Assert.ThrowsAsync<ArgumentException>(() => _database!.RenameAsync("a.png", "x/y.png", Token));
    }

    [Fact]
    public async Task DuplicatingGivesNewGuidsAndKeepsSettings()
    {
        _folder.Write("ui/button.png", "png");
        await ScanAsync();
        await _database!.SetImportSettingsAsync("ui/button.png", new TextureImportSettings { Filter = TextureFilter.Nearest }, Token);

        var copy = await _database.DuplicateAsync("ui", Token);

        Assert.Equal("ui 1", copy.Path);
        Assert.True(_database.TryGetAsset("ui 1/button.png", out var button));
        Assert.NotEqual(_folder.ReadMeta("ui/button.png").Guid, button.Guid);
        Assert.Equal(TextureFilter.Nearest, button.Meta.GetSettings<TextureImportSettings>().Filter);
        Assert.Empty(_database.GetReport().DuplicateGuids);
    }

    [Fact]
    public async Task DeletingMovesTheAssetAndItsMetaToTheTrash()
    {
        _folder.Write("old.png", "png");
        await ScanAsync();
        var guid = _folder.ReadMeta("old.png").Guid;
        _changes.Clear();

        await _database!.DeleteAsync("old.png", Token);

        Assert.False(File.Exists(_folder.FullPath("old.png")));
        Assert.False(File.Exists(_folder.FullPath("old.png.meta")));
        var trashed = Directory.GetFiles(Path.Combine(_folder.Root, ".talesmith", "trash"), "*", SearchOption.AllDirectories).Select(Path.GetFileName);
        Assert.Equal(["old.png", "old.png.meta"], trashed.Order());
        var deleted = Assert.Single(_changes);
        Assert.Equal(AssetChangeKind.Deleted, deleted.Change);
        Assert.Equal(guid, deleted.Guid);
        Assert.False(_database.Catalog.TryGetPath(guid, out _));
    }

    [Fact]
    public async Task CreatingAFolderWritesItsMeta()
    {
        await ScanAsync();

        var folder = await _database!.CreateFolderAsync("levels/forest", Token);

        Assert.True(folder.IsFolder);
        Assert.True(_folder.ReadMeta("levels").IsFolder);
        Assert.True(_database.TryGetAsset("levels", out _));
        Assert.Equal(["forest"], _database.GetChildren("levels").Select(child => child.Name));
    }

    [Fact]
    public async Task ContentAndSettingsChangesAreReportedAndDecideReimports()
    {
        _folder.Write("hero.png", "v1");
        await ScanAsync();
        Assert.True(_database!.TryGetAsset("hero.png", out var record));
        Assert.True(record.NeedsImport);
        await _database.MarkImportedAsync(record.Guid, Token);
        Assert.True(_database.TryGetAsset("hero.png", out record));
        Assert.False(record.NeedsImport);
        _changes.Clear();

        _folder.Write("hero.png", "version 2");
        await _database.RefreshAsync(["hero.png"], Token);
        await _database.SetImportSettingsAsync("hero.png", new TextureImportSettings { Mipmaps = true }, Token);

        Assert.Collection(
            _changes,
            change => Assert.Equal(AssetChangeReasons.Content, change.Reasons),
            change => Assert.Equal(AssetChangeReasons.Settings, change.Reasons));
        Assert.True(_database.TryGetAsset("hero.png", out record));
        Assert.True(record.NeedsImport);
    }

    [Fact]
    public async Task ALostMetaFileIsRestoredWithTheSameGuid()
    {
        _folder.Write("hero.png", "png");
        await ScanAsync();
        var guid = _folder.ReadMeta("hero.png").Guid;

        File.Delete(_folder.FullPath("hero.png.meta"));
        await _database!.RefreshAsync(["hero.png"], Token);

        Assert.Equal(guid, _folder.ReadMeta("hero.png").Guid);
    }

    [Fact]
    public async Task UnreadableMetaFilesAreReplacedAndReported()
    {
        _folder.Write("hero.png", "png");
        _folder.Write("hero.png.meta", "{ broken");

        await ScanAsync();

        var invalid = Assert.Single(_database!.GetReport().InvalidMetas);
        Assert.Equal("hero.png", invalid.Path);
        Assert.True(File.Exists(_folder.FullPath(".hero.png.meta.invalid")));
        Assert.False(_folder.ReadMeta("hero.png").Guid.IsEmpty);
    }

    [Fact]
    public async Task TheDependencyGraphFollowsGuidsAndPaths()
    {
        var texture = AssetGuid.NewGuid();
        var shader = AssetGuid.NewGuid();
        var missing = AssetGuid.NewGuid();
        var entity = Guid.NewGuid().ToString("N");
        _folder.Write("hero.png", "png");
        _folder.WriteMeta("hero.png", new AssetMeta(texture));
        _folder.Write("glow.tshader", """{ "skSlFile": "glow.sksl" }""");
        _folder.WriteMeta("glow.tshader", new AssetMeta(shader));
        _folder.Write("glow.sksl", "uniform shader image; half4 main(float2 c) { return image.eval(c); }");
        _folder.Write("glow.tmaterial", $$"""{ "blend": "additive", "shader": "{{shader}}" }""");
        _folder.Write("level.tscene", $$"""
            { "entities": [
              { "id": "{{entity}}", "components": [ { "type": "Sprite", "data": { "texture": "{{texture}}" } } ] },
              { "id": "{{Guid.NewGuid():N}}", "parent": "{{entity}}", "components": [ { "type": "Sprite", "data": { "texture": "{{missing}}" } } ] }
            ] }
            """);
        _folder.Write("island.hexy", """{ "format": "hexy-map", "version": 1, "tilesets": [ { "id": 1, "name": "Ground", "image": "tiles/ground.png" } ] }""");

        await ScanAsync();
        var database = _database!;
        Assert.True(database.TryGetAsset("level.tscene", out var scene));
        Assert.True(database.TryGetAsset("glow.tmaterial", out var material));
        Assert.True(database.TryGetAsset("glow.sksl", out var source));
        Assert.True(database.TryGetAsset("island.hexy", out var map));

        Assert.Equal([texture], database.GetDependencies(scene.Guid));
        Assert.Equal([shader], database.GetDependencies(material.Guid));
        Assert.Equal([material.Guid], database.GetDependents(shader));
        Assert.Equal([shader, material.Guid], database.GetDependents(source.Guid, transitive: true));

        var report = database.GetReport();
        Assert.Contains(report.MissingReferences, reference => reference.From == scene.Guid && reference.Reference.Guid == missing);
        Assert.Contains(report.MissingReferences, reference => reference.From == map.Guid && reference.Reference.Path == "tiles/ground.png");
        Assert.DoesNotContain(report.MissingReferences, reference => reference.Reference.Guid.ToString() == entity);
        Assert.Contains(report.UnreferencedAssets, asset => asset.Path == "glow.tmaterial");
        Assert.DoesNotContain(report.UnreferencedAssets, asset => asset.Path == "hero.png");

        _folder.Write("tiles/ground.png", "png");
        await database.RefreshAsync(["tiles"], Token);
        Assert.Contains(database.GetDependents(database.Catalog.TryGetGuid("tiles/ground.png", out var ground) ? ground : default), dependent => dependent == map.Guid);
    }

    [Fact]
    public async Task MetaFilesWithoutAssetsAreReportedAsOrphans()
    {
        _folder.WriteMeta("gone.png", new AssetMeta(AssetGuid.NewGuid()));
        await ScanAsync();

        Assert.Equal(["gone.png.meta"], _database!.GetReport().OrphanedMetas);
        Assert.Equal(1, await _database.RemoveOrphanedMetasAsync(Token));
        Assert.Empty(_database.GetReport().OrphanedMetas);
    }

    [Fact]
    public async Task TheCacheAvoidsHashingUnchangedFilesAndRemembersImports()
    {
        _folder.Write("hero.png", "png");
        await ScanAsync();
        Assert.True(_database!.TryGetAsset("hero.png", out var record));
        await _database.MarkImportedAsync(record.Guid, Token);
        await _database.DisposeAsync();

        _database = CreateDatabase();
        await _database.ScanAsync(cancellationToken: Token);

        Assert.True(_database.TryGetAsset("hero.png", out var reloaded));
        Assert.Equal(record.ContentHash, reloaded.ContentHash);
        Assert.False(reloaded.NeedsImport);
    }

    [Fact]
    public async Task WatchingPairsAMoveMadeOutsideTheEditor()
    {
        _folder.Write("hero.png", "png");
        Directory.CreateDirectory(_folder.FullPath("sprites"));
        await ScanAsync();
        var guid = _folder.ReadMeta("hero.png").Guid;
        var moved = new TaskCompletionSource<AssetChange>(TaskCreationOptions.RunContinuationsAsynchronously);
        _database!.AssetsChanged += (_, e) =>
        {
            if (e.Changes.FirstOrDefault(change => change.Change == AssetChangeKind.Moved) is { } change)
                moved.TrySetResult(change);
        };
        _database.StartWatching();

        File.Move(_folder.FullPath("hero.png"), _folder.FullPath("sprites/hero.png"));
        File.Move(_folder.FullPath("hero.png.meta"), _folder.FullPath("sprites/hero.png.meta"));

        var change = await moved.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        Assert.Equal(guid, change.Guid);
        Assert.Equal("sprites/hero.png", change.Path);
        Assert.Equal("hero.png", change.OldPath);
    }

    [Fact]
    public async Task WatchingPairsAMoveWithoutTheMetaByContent()
    {
        _folder.Write("hero.png", "unique contents");
        await ScanAsync();
        var guid = _folder.ReadMeta("hero.png").Guid;

        File.Move(_folder.FullPath("hero.png"), _folder.FullPath("renamed.png"));
        await _database!.RefreshAsync(["hero.png", "renamed.png"], Token);

        Assert.Equal(guid, _folder.ReadMeta("renamed.png").Guid);
        Assert.False(File.Exists(_folder.FullPath("hero.png.meta")));
    }

    [Fact]
    public async Task WatchingReportsNewFiles()
    {
        await ScanAsync();
        var added = new TaskCompletionSource<AssetChange>(TaskCreationOptions.RunContinuationsAsynchronously);
        _database!.AssetsChanged += (_, e) =>
        {
            if (e.Changes.FirstOrDefault(change => change is { Change: AssetChangeKind.Added, Path: "new.png" }) is { } change)
                added.TrySetResult(change);
        };
        _database.StartWatching();

        _folder.Write("new.png", "png");

        var change = await added.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        Assert.Equal(AssetKind.Texture, change.Kind);
        Assert.True(File.Exists(_folder.FullPath("new.png.meta")));
    }

    [Fact]
    public async Task ScanningReportsProgress()
    {
        for (var i = 0; i < 10; i++)
            _folder.Write($"file{i}.txt", i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var reports = new ConcurrentQueue<AssetScanProgress>();
        _database = CreateDatabase();
        _database.ScanProgressChanged += (_, progress) => reports.Enqueue(progress);

        await _database.ScanAsync(cancellationToken: Token);

        Assert.Equal(10, reports.Last().Total);
        Assert.Equal(10, reports.Last().Processed);
    }

    [Fact]
    public async Task TheIndexListsEveryAsset()
    {
        _folder.Write("a/b.png", "png");
        await ScanAsync();

        var index = _database!.CreateIndex();

        Assert.Equal(["a", "a/b.png"], index.Entries.Select(entry => entry.Path));
    }

    private AssetDatabase CreateDatabase(string pluginsFolder = "plugins")
    {
        var state = Path.Combine(_folder.Root, AssetDatabaseOptions.StateFolderName);
        var options = new AssetDatabaseOptions(_folder.AssetRoot)
        {
            CacheFolder = Path.Combine(state, "cache"),
            TrashFolder = Path.Combine(state, "trash"),
            PluginsFolder = pluginsFolder,
            WatchDebounce = TimeSpan.FromMilliseconds(100)
        };
        var database = new AssetDatabase(options, TestAssets.Importers, NullLogger<AssetDatabase>.Instance);
        database.AssetsChanged += (_, e) =>
        {
            foreach (var change in e.Changes)
                _changes.Enqueue(change);
        };
        return database;
    }

    private async Task<AssetScanResult> ScanAsync()
    {
        _database ??= CreateDatabase();
        return await _database.ScanAsync(cancellationToken: Token);
    }
}
