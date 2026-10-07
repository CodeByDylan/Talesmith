using Microsoft.Extensions.Logging.Abstractions;
using Talesmith.Assets.Database;
using Talesmith.Assets.Textures;
using Talesmith.Events;
using Talesmith.Mathematics;
using Talesmith.Rendering;

namespace Talesmith.Assets.Tests;

public sealed class AssetManagerTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public AssetManagerTests() => Directory.CreateDirectory(_folder.AssetRoot);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task AssetsLoadByGuid()
    {
        var guid = AssetGuid.NewGuid();
        _folder.Write("hero.png", TestAssets.Png(4, 4, Color.White));
        var catalog = new AssetCatalog();
        catalog.Set("hero.png", new AssetMeta(guid));
        using var manager = TestAssets.Manager(_folder.AssetRoot, catalog);

        var texture = await manager.LoadAsync<TextureAsset>(guid, Token);

        Assert.Same(texture, await manager.LoadAsync<TextureAsset>("hero.png", Token));
        Assert.Equal(guid, manager.GetGuid(texture));
        await Assert.ThrowsAsync<AssetException>(() => manager.LoadAsync<TextureAsset>(AssetGuid.NewGuid(), Token));
    }

    [Fact]
    public async Task ReleasingTheLastReferenceUnloadsTheAssetAndWhatItLoaded()
    {
        _folder.Write("tile.png", TestAssets.Png(4, 4, Color.White));
        _folder.Write("tiles.tatlas", """{ "sources": [ "tile.png" ] }""");
        using var manager = TestAssets.Manager(_folder.AssetRoot);

        var first = await manager.LoadAsync<TextureAsset>("tiles.tatlas", Token);
        await manager.LoadAsync<TextureAsset>("tiles.tatlas", Token);
        Assert.Equal(2, manager.GetReferenceCount("tiles.tatlas"));
        Assert.Equal(1, manager.GetReferenceCount("tile.png"));

        Assert.True(manager.Release(first));
        Assert.True(manager.IsLoaded("tiles.tatlas"));
        Assert.True(manager.Release(first));

        Assert.False(manager.IsLoaded("tiles.tatlas"));
        Assert.False(manager.IsLoaded("tile.png"));
        Assert.False(manager.Release(first));
    }

    [Fact]
    public async Task ReloadingReplacesTheAssetAndRaisesEventsOnTheEventBus()
    {
        _folder.Write("hero.png", TestAssets.Png(4, 4, Color.White));
        var events = new EventBus();
        var reloads = new List<AssetReloaded>();
        var imports = new List<AssetImported>();
        events.Subscribe((ref AssetReloaded e) => reloads.Add(e));
        events.Subscribe((ref AssetImported e) => imports.Add(e));
        using var manager = TestAssets.Manager(_folder.AssetRoot, events: events);
        var old = await manager.LoadAsync<TextureAsset>("hero.png", Token);

        _folder.Write("hero.png", TestAssets.Png(8, 4, Color.White));
        Assert.True(await manager.ReloadAsync("hero.png", Token));
        Assert.Empty(reloads);
        events.DispatchQueued();

        var reloaded = Assert.Single(reloads);
        Assert.Same(old, reloaded.OldAsset);
        var replacement = Assert.IsType<TextureAsset>(reloaded.NewAsset);
        Assert.Equal(8, replacement.Width);
        Assert.True(manager.TryGet<TextureAsset>("hero.png", out var current));
        Assert.Same(replacement, current);
        Assert.Equal([false, true], imports.Select(import => import.IsReload));
        Assert.Equal(1, manager.GetReferenceCount("hero.png"));
    }

    [Fact]
    public async Task ReloadingCascadesToAssetsThatImportedIt()
    {
        _folder.Write("tile.png", TestAssets.Png(4, 4, Color.White));
        _folder.Write("tiles.tatlas", """{ "sources": [ "tile.png" ] }""");
        var events = new EventBus();
        var reloaded = new List<string>();
        events.Subscribe((ref AssetReloaded e) => reloaded.Add(e.Path));
        using var manager = TestAssets.Manager(_folder.AssetRoot, events: events);
        await manager.LoadAsync<TextureAsset>("tiles.tatlas", Token);

        _folder.Write("tile.png", TestAssets.Png(6, 6, Color.White));
        await manager.ReloadAsync("tile.png", Token);
        events.DispatchQueued();

        Assert.Equal(["tile.png", "tiles.tatlas"], reloaded);
        Assert.True(manager.TryGet<TextureAsset>("tiles.tatlas", out var atlas));
        Assert.Equal(6, atlas.Sprites[0].Rect.Width);
    }

    [Fact]
    public async Task AFailedReloadKeepsTheOldAsset()
    {
        _folder.Write("hero.png", TestAssets.Png(4, 4, Color.White));
        using var manager = TestAssets.Manager(_folder.AssetRoot);
        var old = await manager.LoadAsync<TextureAsset>("hero.png", Token);

        _folder.Write("hero.png", "not a png");

        Assert.False(await manager.ReloadAsync("hero.png", Token));
        Assert.True(manager.TryGet<TextureAsset>("hero.png", out var current));
        Assert.Same(old, current);
    }

    [Fact]
    public async Task MovedAssetsStayLoadedUnderTheirNewPath()
    {
        _folder.Write("hero.png", TestAssets.Png(4, 4, Color.White));
        using var manager = TestAssets.Manager(_folder.AssetRoot);
        var texture = await manager.LoadAsync<TextureAsset>("hero.png", Token);

        manager.NotifyMoved("hero.png", "sprites/hero.png");

        Assert.True(manager.TryGet<TextureAsset>("sprites/hero.png", out var moved));
        Assert.Same(texture, moved);
        Assert.True(manager.TryGetPath(texture, out var path));
        Assert.Equal("sprites/hero.png", path);
    }

    [Fact]
    public async Task ImportersReceiveSettingsFromTheCatalog()
    {
        _folder.Write("hero.png", TestAssets.Png(4, 4, Color.White));
        var catalog = new AssetCatalog();
        catalog.Set("hero.png", new AssetMeta(AssetGuid.NewGuid()).WithSettings(new TextureImportSettings { Filter = TextureFilter.Nearest }));
        using var manager = TestAssets.Manager(_folder.AssetRoot, catalog);

        var texture = await manager.LoadAsync<TextureAsset>("hero.png", Token);

        Assert.Equal(TextureFilter.Nearest, texture.Settings.Filter);
    }

    [Fact]
    public async Task HotReloadFollowsTheDatabase()
    {
        _folder.Write("hero.png", TestAssets.Png(4, 4, Color.White));
        await using var database = new AssetDatabase(new AssetDatabaseOptions(_folder.AssetRoot), TestAssets.Importers, NullLogger<AssetDatabase>.Instance);
        await database.ScanAsync(cancellationToken: Token);
        var events = new EventBus();
        using var manager = TestAssets.Manager(_folder.AssetRoot, database.Catalog, events);
        using var hotReload = new AssetHotReload(database, manager, NullLogger<AssetHotReload>.Instance);
        await manager.LoadAsync<TextureAsset>("hero.png", Token);

        await database.SetImportSettingsAsync("hero.png", new TextureImportSettings { Filter = TextureFilter.Nearest }, Token);
        await hotReload.Idle;

        Assert.True(manager.TryGet<TextureAsset>("hero.png", out var texture));
        Assert.Equal(TextureFilter.Nearest, texture.Settings.Filter);
        Assert.True(database.TryGetAsset("hero.png", out var record));
        Assert.False(record.NeedsImport);

        await database.MoveAsync("hero.png", "player.png", Token);
        await hotReload.Idle;
        Assert.True(manager.IsLoaded("player.png"));
    }
}
