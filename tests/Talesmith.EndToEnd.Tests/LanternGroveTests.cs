using System.Numerics;
using Talesmith.Ecs;
using Talesmith.Input;
using Talesmith.Runtime.Components;
using Talesmith.Scripting.Compiler;
using Talesmith.Systems;

namespace Talesmith.EndToEnd.Tests;

/// <summary>Plays the Lantern Grove sample with its scripts compiled the way the editor compiles them, driving the player with keys.</summary>
public sealed class LanternGroveTests
{
    private static string Project => Workspace.Sample("LanternGrove");

    [Fact]
    public async Task ThePlayerRunsJumpsAndCollectsTheFirstLantern()
    {
        using var compiler = new ScriptCompiler(new ScriptCompilerOptions { ProjectDirectory = Project });
        var compilation = await compiler.CompileAsync(TestContext.Current.CancellationToken);
        Assert.True(compilation.Success, string.Join("\n", compilation.Errors));
        Assert.Empty(compilation.Warnings);
        using var loggers = new RecordingLoggers();

        await using var game = await HeadlessGame.StartAsync(Path.Combine(Project, "assets"), loggers.Factory, compilation.Load());
        Assert.Equal(ExecutionModes.Play, game.Game.Mode);
        var player = game.Find("Player");
        var lantern = game.Find("Lantern 1");
        Assert.False(player.IsNull);
        Assert.False(lantern.IsNull);
        var start = Position(game.World, player);
        var highest = start.Y;

        game.Run(300, beforeFrame: frame =>
        {
            if (frame == 10)
                game.Input.KeyDown(Key.Right, KeyModifiers.None);
            if (frame == 40)
                game.Input.KeyDown(Key.Space, KeyModifiers.None);
            if (frame == 55)
                game.Input.KeyUp(Key.Space, KeyModifiers.None);
            highest = MathF.Min(highest, Position(game.World, player).Y);
        });

        Assert.True(Position(game.World, player).X > start.X + 400, $"The player should have run right from {start}.");
        Assert.True(highest < start.Y - 100, $"The player should have jumped; the highest point was {highest}.");
        Assert.True(!game.World.IsAlive(lantern) || !game.World.Get<Sprite>(lantern).Visible, "The first lantern should have been collected.");
        Assert.Equal(6, Lanterns(game));
        Assert.Empty(loggers.Problems);
    }

    private static Vector2 Position(World world, Entity entity) => world.Get<Transform>(entity).Position;

    private static int Lanterns(HeadlessGame game)
    {
        var count = 0;
        foreach (var archetype in game.World.Query<Tags, Sprite>())
        {
            var tags = archetype.GetSpan<Tags>();
            var sprites = archetype.GetSpan<Sprite>();
            for (var i = 0; i < tags.Length; i++)
            {
                if (tags[i].Has("lantern") && sprites[i].Visible)
                    count++;
            }
        }

        return count;
    }
}
