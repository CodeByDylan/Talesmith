using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Talesmith.Avalonia.Hosting;
using Talesmith.Editor.Console;
using Talesmith.Editor.Hosting;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Projects.Templates;
using Talesmith.Editor.Settings;
using Talesmith.Editor.Shell;

namespace Talesmith.Editor.Tests.Projects;

/// <summary>Opens projects headless and follows the overlay that covers the workspace while they open.</summary>
public sealed class ProjectLoadingTests
{
    [Fact]
    public void TheOverlayWalksThroughTheStepsAndGoesOnceTheSceneIsShown() => Headless.Run(async () =>
    {
        ProjectLoadingViewModel? loading = null;
        var states = new List<string>();
        await using var fixture = await EditorFixture.OpenAsync("hex-adventure", beforeStart: services =>
        {
            loading = services.GetRequiredService<ProjectLoadingViewModel>();
            Assert.True(loading.IsOpen);
            Assert.Equal(LoadingStepState.Active, loading.Engine.State);
            Assert.Equal(LoadingStepState.Pending, loading.Assets.State);
            Assert.Equal(LoadingStepState.Pending, loading.Scene.State);
            foreach (var (step, name) in loading.Steps.Zip(["engine", "assets", "scene"]))
            {
                step.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(LoadingStepViewModel.State))
                        states.Add($"{name} {step.State}");
                };
            }
        });

        await fixture.WaitAsync(() => !loading!.IsOpen);

        Assert.All(loading!.Steps, step => Assert.True(step.IsDone, step.Title));
        Assert.Equal("Fixture", loading.Name);
        Assert.Equal("Loading main.tscene", loading.Scene.Title);
        Assert.Matches(@"^\d+ assets$", loading.Assets.Detail);
        Assert.Null(loading.Error);
        Assert.Equal(["engine Done", "assets Active", "assets Done", "scene Active", "scene Done"], states.Distinct());
    });

    [Fact]
    public void AProjectThatCannotOpenShowsWhyUntilTheUserMovesOn() => Headless.Run(async () =>
    {
        var root = Path.Combine(Path.GetTempPath(), "talesmith-tests", Guid.NewGuid().ToString("N"));
        var creator = new ProjectCreator([new EmptyTemplate()]);
        var folder = await creator.CreateAsync(creator.Templates[0], root, "Broken");
        await using var application = new ServiceCollection()
            .AddTalesmithEditorApplication(new JsonSettingsService(null), new FailingSessions())
            .BuildServiceProvider();
        await using var services = await application.GetRequiredService<EditorHost>().BuildAsync(new EditorProject(folder), new EditorConsole());
        var loading = services.GetRequiredService<ProjectLoadingViewModel>();
        try
        {
            services.GetRequiredService<ProjectService>().Start();
            await WaitUntilAsync(() => loading.HasFailed);

            Assert.Equal(FailingSessions.Reason, loading.Error);
            Assert.True(loading.IsOpen);
            Assert.False(loading.IsLoading);
            Assert.Equal(LoadingStepState.Failed, loading.Engine.State);
            Assert.Equal(LoadingStepState.Pending, loading.Scene.State);

            loading.ShowConsoleCommand.Execute(null);
            Assert.False(loading.IsOpen);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    });

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(30))
                throw new TimeoutException("The overlay did not reach the expected state.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Fails to start any game, as a machine without a working renderer would.</summary>
    private sealed class FailingSessions : IGameSessionFactory
    {
        public const string Reason = "No renderer could start on this machine.";

        public Task<GameSession> CreateAsync(DesktopGameOptions options, ILoggerFactory loggers, CancellationToken cancellationToken) =>
            Task.FromException<GameSession>(new InvalidOperationException(Reason));
    }
}
