using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Input;
using Talesmith.Runtime.Hosting;
using Talesmith.Samples.Shared;
using static Talesmith.Samples.IsleHopper.Tests.IsleHopperGame;

namespace Talesmith.Samples.IsleHopper.Tests;

/// <summary>Plays Isle Hopper on a simulation thread, as the player runs it, with input and UI on other threads.</summary>
public sealed class SimulationThreadTests
{
    [Fact]
    public async Task TheHeroJumpsAndLandsWithInputFromAnotherThread()
    {
        await using var game = await StartAsync();
        var input = game.Services.GetRequiredService<IInputSink>();
        new SimulationThread(game).Start();

        input.KeyDown(Key.Space, KeyModifiers.None);
        await WaitUntilAsync(() => game.InvokeAsync(() => !Hero(game).OnGround));
        input.KeyUp(Key.Space, KeyModifiers.None);
        await WaitUntilAsync(() => game.InvokeAsync(() => Hero(game).OnGround));
    }

    [Fact]
    public async Task TheHudSnapshotShowsTheLevel()
    {
        await using var game = await StartAsync();
        var hud = game.Services.GetRequiredService<IsleHud>().View.Value;

        Assert.False(string.IsNullOrEmpty(hud.Title));
        Assert.True(hud.GemsTotal > 0);
        Assert.Equal(0, hud.Gems);
        Assert.False(hud.IsComplete);
    }

    [Fact]
    public async Task TheMenuKeyOpensThePauseMenuAndPausesTheGame()
    {
        await using var game = await StartAsync();
        var menu = game.Services.GetRequiredService<GameMenu>();
        new SimulationThread(game).Start();

        game.Services.GetRequiredService<IInputSink>().KeyDown(Key.Escape, KeyModifiers.None);

        await WaitUntilAsync(() => game.InvokeAsync(() => game.IsPaused));
        Assert.True(menu.IsOpen);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!await condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(10))
                throw new TimeoutException("The game did not reach the expected state.");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }
}
