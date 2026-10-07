using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Talesmith.Assets;
using Talesmith.Assets.Maps;
using Talesmith.Grids;
using Talesmith.Runtime.Hosting;
using Talesmith.Runtime.Scenes;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Runtime.Tests.Scenes;

public sealed class SceneLoadProgressTests : IAsyncDisposable
{
    private const string ScenePath = "scenes/main.tscene";

    private readonly MemoryAssetSource _files = new();
    private readonly FakeCatalog _catalog = new();
    private readonly GatedMapImporter _gated = new();
    private Game? _game;

    public SceneLoadProgressTests()
    {
        var quick = _catalog.Add("maps/quick.testmap");
        var slow = _catalog.Add("maps/slow.gatedmap");
        _files.Write("maps/quick.testmap", "");
        _files.Write("maps/slow.gatedmap", "");
        var scene = SceneDocument.Create();
        foreach (var map in new[] { quick, slow, quick })
            scene.Entities.Add(new EntityDocument { Id = Guid.NewGuid(), Components = [new ComponentDocument("TileMapRenderer", new JsonObject { ["map"] = map.ToString() })] });
        _files.Write(ScenePath, DocumentSerializer.Write(scene));
        _catalog.Add(ScenePath);
    }

    public async ValueTask DisposeAsync()
    {
        _gated.Release();
        if (_game is not null)
            await _game.DisposeAsync();
    }

    [Fact]
    public void ProgressIsUnknownUntilWorkIsFound()
    {
        var progress = new SceneLoadProgress();
        Assert.Null(progress.Fraction);

        progress.Expect(4);
        progress.Advance();
        Assert.Equal(0.25, progress.Fraction);

        progress.Advance(5);
        Assert.Equal(1, progress.Fraction);
        Assert.Throws<ArgumentOutOfRangeException>(() => progress.Expect(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => progress.Advance(-1));
    }

    [Fact]
    public async Task ADocumentSceneCountsEachAssetItUsesOnceAsItLoads()
    {
        var game = CreateGame(new GameSettings());
        Assert.Null(game.Scenes.LoadProgress);

        var load = game.Scenes.LoadAsync(DocumentScene.ForPath(ScenePath), SceneTransition.Instant, TestContext.Current.CancellationToken);
        await _gated.Started.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var progress = Assert.IsType<SceneLoadProgress>(game.Scenes.LoadProgress);
        await WaitUntilAsync(() => progress.Loaded == 1);

        Assert.True(game.Scenes.IsLoading);
        Assert.Equal(2, progress.Total);
        Assert.Equal(0.5, progress.Fraction);

        _gated.Release();
        await load;
        Assert.Null(game.Scenes.LoadProgress);
        Assert.False(game.Scenes.IsLoading);
        Assert.Same(progress, game.Scenes.Current!.LoadProgress);
        Assert.Equal(1, progress.Fraction);
    }

    [Fact]
    public async Task WhenStartedCompletesOnceTheStartSceneHasFadedIn()
    {
        _gated.Release();
        var game = CreateGame(new GameSettings { StartScene = DocumentScene.ForPath(ScenePath) });

        game.Start();
        await TickUntilAsync(game, () => game.WhenStarted.IsCompleted);

        Assert.True(game.WhenStarted.IsCompletedSuccessfully);
        Assert.IsType<DocumentScene>(game.Scenes.Current);
        Assert.False(game.Scenes.IsLoading);
    }

    [Fact]
    public async Task WhenStartedFaultsWithTheReasonTheStartSceneDidNotLoad()
    {
        var game = CreateGame(new GameSettings { StartScene = new SceneRequest("missing") });

        game.Start();
        await TickUntilAsync(game, () => game.WhenStarted.IsCompleted);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => game.WhenStarted);
        Assert.Contains("'missing'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhenStartedIsCanceledWhenTheGameIsDisposedFirst()
    {
        var game = CreateGame(new GameSettings { StartScene = DocumentScene.ForPath(ScenePath) });
        game.Start();
        game.Tick(1.0 / 60);

        await game.DisposeAsync();
        _game = null;

        Assert.True(game.WhenStarted.IsCanceled);
    }

    private Game CreateGame(GameSettings settings)
    {
        var builder = GameBuilder.Create(AppContext.BaseDirectory, settings);
        builder.Services.AddSingleton<IAssetSource>(_files);
        builder.Services.AddSingleton<IAssetCatalog>(_catalog);
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IAssetImporter, TestMapImporter>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IAssetImporter>(_gated));
        _game = builder.Build();
        return _game;
    }

    private static async Task TickUntilAsync(Game game, Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(10))
                throw new TimeoutException("The game did not reach the expected state.");
            game.Tick(1.0 / 60);
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(10))
                throw new TimeoutException("The load did not reach the expected state.");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Imports ".gatedmap" files as empty maps, but only once released.</summary>
    private sealed class GatedMapImporter : AssetImporter<TileMap>
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override IReadOnlyList<string> Extensions { get; } = [".gatedmap"];

        public Task Started => _started.Task;

        public void Release() => _gate.TrySetResult();

        public override async Task<TileMap> ImportAsync(AssetImportContext context, CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            await _gate.Task.WaitAsync(cancellationToken);
            return new TileMap(context.Path, new SquareLayout(64, 64), 5, [], [new TileLayer("Ground", 5)], PropertySet.Empty);
        }
    }
}
