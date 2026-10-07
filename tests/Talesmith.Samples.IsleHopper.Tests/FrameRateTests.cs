using Microsoft.Extensions.DependencyInjection;
using Talesmith.Input;
using Talesmith.Runtime.Hosting;
using static Talesmith.Samples.IsleHopper.Tests.IsleHopperGame;

namespace Talesmith.Samples.IsleHopper.Tests;

/// <summary>Plays the Isle Hopper level headless at different frame rates, as with VSync off, and checks the hero behaves the same.</summary>
public sealed class FrameRateTests
{
    public static TheoryData<int> FrameRates => [60, 144, 500, 3000];

    [Theory]
    [MemberData(nameof(FrameRates))]
    public async Task HeroStandingStillStaysOnTheGround(int framesPerSecond)
    {
        await using var game = await StartAsync();
        var airborneFrames = 0;
        for (var i = 0; i < framesPerSecond; i++)
        {
            game.Tick(1.0 / framesPerSecond);
            if (!Hero(game).OnGround)
                airborneFrames++;
        }

        Assert.Equal(0, airborneFrames);
    }

    [Fact]
    public async Task JumpsReachTheSameHeightAtEveryFrameRate()
    {
        var heights = new Dictionary<int, float>();
        foreach (var framesPerSecond in new[] { 60, 144, 500, 3000 })
            heights[framesPerSecond] = await JumpAsync(framesPerSecond);

        // Rising at 860 px/s against 2300 px/s² of gravity peaks a little over two cells up.
        Assert.InRange(heights[60], 145, 165);
        Assert.All(heights, height => Assert.Equal(heights[60], height.Value, 0.01f));
    }

    /// <summary>Holds jump for a second, lets the hero land, and returns how high its feet rose.</summary>
    private static async Task<float> JumpAsync(int framesPerSecond)
    {
        await using var game = await StartAsync();
        var start = Feet(game);
        var input = game.Services.GetRequiredService<IInputSink>();
        var highest = start;

        input.KeyDown(Key.Space, KeyModifiers.None);
        for (var i = 0; i < framesPerSecond; i++)
        {
            game.Tick(1.0 / framesPerSecond);
            highest = Math.Min(highest, Feet(game));
        }

        input.KeyUp(Key.Space, KeyModifiers.None);
        for (var i = 0; i < framesPerSecond; i++)
            game.Tick(1.0 / framesPerSecond);

        Assert.True(Hero(game).OnGround, $"The hero did not land at {framesPerSecond} fps.");
        Assert.Equal(start, Feet(game), 0.01f);
        return start - highest;
    }

    /// <summary>The hero's simulated feet, rather than the position drawn between fixed steps.</summary>
    private static float Feet(Game game) => World(game).Get<SmoothMotion>(HeroEntity(game)).Current.Y;
}
