using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Talesmith.Assets.Packs;
using Talesmith.Avalonia.Hosting;
using Talesmith.Avalonia.Loading;
using Talesmith.Avalonia.Presentation;
using Talesmith.Runtime.Hosting;
using Talesmith.Runtime.Scenes;

namespace Talesmith.Avalonia.Tests;

/// <summary>Starts small games in headless windows and watches their loading screens.</summary>
public sealed class LoadingScreenTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("talesmith-loading-");
    private readonly SceneGate _gate = new();

    public LoadingScreenTests() => Directory.CreateDirectory(Path.Combine(_folder.FullName, "config"));

    public void Dispose()
    {
        _gate.Open();
        _folder.Delete(true);
    }

    [Fact]
    public void TheTitleCoversTheGameUntilItsStartSceneIsShown() => HeadlessApp.Run(async () =>
    {
        WriteSettings("""{ "title": "Lantern Trial", "startScene": "quick" }""");
        var session = CreateSession();
        var window = new GameWindow(session, captureDirectory: null);
        window.Show();

        Assert.True(window.LoadingScreen.IsVisible);
        Assert.True(window.LoadingScreen.IsHitTestVisible);
        Assert.Contains("Lantern Trial", Texts(window.LoadingScreen));
        await RefreshUntilAsync(() => window.LoadingScreen.Opacity == 0);
        Assert.False(window.LoadingScreen.IsHitTestVisible);
        await RefreshUntilAsync(() => !window.LoadingScreen.IsVisible);
        Assert.True(session.Game.WhenStarted.IsCompletedSuccessfully);

        window.Close();
    });

    [Fact]
    public void AStartSceneThatCannotLoadShowsWhyAndTheWindowCanBeClosed() => HeadlessApp.Run(async () =>
    {
        WriteSettings("""{ "startScene": "missing" }""");
        var window = new GameWindow(CreateSession(), captureDirectory: null);
        window.Show();

        await RefreshUntilAsync(() => VisibleTexts(window.LoadingScreen).Contains("The game could not start"));
        Assert.Contains(Texts(window.LoadingScreen), text => text.Contains("'missing'", StringComparison.Ordinal));
        var close = window.LoadingScreen.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Close"));
        close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        await RefreshUntilAsync(() => !window.IsVisible);
    });

    [Fact]
    public void ASlowSceneChangeBringsTheLoadingScreenBackWithItsProgress() => HeadlessApp.Run(async () =>
    {
        WriteSettings("""{ "startScene": "quick" }""");
        var session = CreateSession();
        var window = new GameWindow(session, captureDirectory: null);
        window.Show();
        await RefreshUntilAsync(() => !window.LoadingScreen.IsVisible);

        await LoadSlowSceneAsync(session.Game);
        await RefreshUntilAsync(() => window.LoadingScreen.IsVisible && Bar(window).Value == 0.25);
        Assert.True(window.LoadingScreen.IsHitTestVisible);

        _gate.Open();
        await RefreshUntilAsync(() => !window.LoadingScreen.IsVisible);
        Assert.IsType<SlowScene>(session.Game.Scenes.Current);
        window.Close();
    });

    [Fact]
    public void ASceneChangeRightAfterTheStartBringsTheLoadingScreenBack() => HeadlessApp.Run(async () =>
    {
        WriteSettings("""{ "startScene": "quick" }""");
        var session = CreateSession();
        var window = new GameWindow(session, captureDirectory: null);
        window.Show();
        await RefreshUntilAsync(() => session.Game.WhenStarted.IsCompleted);

        await LoadSlowSceneAsync(session.Game);
        var clock = Stopwatch.StartNew();
        await RefreshUntilAsync(() => clock.Elapsed > TimeSpan.FromSeconds(1.5));

        Assert.True(window.LoadingScreen.IsVisible);
        Assert.Equal(1, window.LoadingScreen.Opacity);
        Assert.Equal(0.25, Bar(window).Value);
        _gate.Open();
        await RefreshUntilAsync(() => !window.LoadingScreen.IsVisible);
        window.Close();
    });

    [Fact]
    public void GamesCanKeepTheLoadingScreenToTheirStart() => HeadlessApp.Run(async () =>
    {
        WriteSettings("""{ "startScene": "quick", "loadingScreen": { "betweenScenes": false } }""");
        var session = CreateSession();
        var window = new GameWindow(session, captureDirectory: null);
        window.Show();
        await RefreshUntilAsync(() => !window.LoadingScreen.IsVisible);

        await LoadSlowSceneAsync(session.Game);
        var clock = Stopwatch.StartNew();
        await RefreshUntilAsync(() => clock.Elapsed > TimeSpan.FromSeconds(1.5));

        Assert.False(window.LoadingScreen.IsVisible);
        Assert.True(session.Game.Scenes.IsLoading);
        window.Close();
    });

    [Fact]
    public void AnImageFromTheGamesAssetsReplacesTheTitleAndPixelArtScalesByWholeSteps() => HeadlessApp.Run(async () =>
    {
        WriteImage("ui/logo.png", 40, 20);
        WriteSettings("""{ "title": "Lantern Trial", "textureFilter": "nearest", "loadingScreen": { "image": "ui/logo.png" } }""");
        var window = new GameWindow(GameSettings.Load(_folder.FullName), PackAssetSource.OpenFolder(_folder.FullName), captureDirectory: null)
        {
            Width = 1000,
            Height = 600
        };
        window.Show();

        var image = window.LoadingScreen.GetVisualDescendants().OfType<Image>().Single();
        await RefreshUntilAsync(() => image.IsVisible && !double.IsNaN(image.Width));
        Assert.DoesNotContain("Lantern Trial", VisibleTexts(window.LoadingScreen));
        var source = image.Source!.Size;
        var steps = image.Width / source.Width;
        Assert.Equal(Math.Floor(steps), steps);
        Assert.Equal(source.Height * steps, image.Height);
        Assert.True(source.Width * (steps + 1) > 1000 * 0.7 || source.Height * (steps + 1) > 600 * 0.6, "One more step would still fit.");

        window.Close();
    });

    [Fact]
    public void AMissingImageFallsBackToTheTitle() => HeadlessApp.Run(async () =>
    {
        WriteSettings("""{ "title": "Lantern Trial", "loadingScreen": { "image": "ui/missing.png" } }""");
        var window = new GameWindow(GameSettings.Load(_folder.FullName), PackAssetSource.OpenFolder(_folder.FullName), captureDirectory: null);
        window.Show();

        await RefreshUntilAsync(() => VisibleTexts(window.LoadingScreen).Contains("Lantern Trial"));
        window.Close();
    });

    private GameSession CreateSession() => GameSession.Create(new DesktopGameOptions
    {
        AssetRoot = _folder.FullName,
        Renderer = RendererPreference.Skia,
        Audio = false,
        DeveloperTools = false,
        Configure = builder =>
        {
            builder.Services.AddSingleton(_gate);
            builder.Services.AddScene<QuickScene>("quick");
            builder.Services.AddScene<SlowScene>("slow");
        }
    }, NullLoggerFactory.Instance, WindowGraphics.None);

    private void WriteSettings(string json) => File.WriteAllText(Path.Combine(_folder.FullName, GameSettings.FileName), json);

    private void WriteImage(string path, int width, int height)
    {
        var file = Path.Combine(_folder.FullName, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(new SKColor(240, 180, 60));
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(file, data.ToArray());
    }

    /// <summary>Starts loading the slow scene on the game thread without waiting for it.</summary>
    private static Task LoadSlowSceneAsync(Game game) => game.InvokeAsync(() => { _ = game.Scenes.LoadAsync(new SceneRequest("slow")); });

    private static LoadingBar Bar(GameWindow window) => window.LoadingScreen.GetVisualDescendants().OfType<LoadingBar>().Single();

    private static List<string> Texts(Control control) =>
        control.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).OfType<string>().ToList();

    private static List<string> VisibleTexts(Control control) =>
        control.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).OfType<string>().ToList();

    private static async Task RefreshUntilAsync(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(30))
                throw new TimeoutException("The loading screen did not reach the expected state.");
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(16, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Holds the slow scene's load until a test opens it.</summary>
    public sealed class SceneGate
    {
        private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Opened => _open.Task;

        public void Open() => _open.TrySetResult();
    }

    private sealed class QuickScene : Scene
    {
    }

    /// <summary>Reports a quarter of its work, then waits for the gate.</summary>
    private sealed class SlowScene(SceneGate gate) : Scene
    {
        protected override async Task LoadAsync(CancellationToken cancellationToken)
        {
            LoadProgress.Expect(4);
            LoadProgress.Advance();
            await gate.Opened.WaitAsync(cancellationToken);
            LoadProgress.Advance(3);
        }
    }
}
