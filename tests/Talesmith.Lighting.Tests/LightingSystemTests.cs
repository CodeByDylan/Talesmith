using System.Numerics;
using Talesmith.Mathematics;
using Talesmith.Rendering;
using Talesmith.Rendering.Lighting;
using Talesmith.Rendering.Skia;
using Talesmith.Rendering.Vulkan;
using Talesmith.Runtime.Components;
using Talesmith.Systems;

namespace Talesmith.Lighting.Tests;

/// <summary>Runs lit scenes through the game loop and the renderers.</summary>
public sealed class LightingSystemTests
{
    public static TheoryData<string> Backends
    {
        get
        {
            var backends = new TheoryData<string> { "skia" };
            try
            {
                using var vulkan = VulkanRenderer.Create();
                backends.Add("vulkan");
            }
            catch (VulkanUnavailableException)
            {
            }

            return backends;
        }
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task DungeonIsLitByTorchesAndShadowedByWalls(string backend)
    {
        await using var game = new LitGame(CreateRenderer(backend), Dungeon.Build);
        Dungeon.Darken(game.Environment);
        var image = game.Render();
        LitGame.Save(image, $"dungeon-{backend}");

        var screen = ScreenOf(game);
        var nearTorch = LitGame.Brightness(image, (int)(Dungeon.Torch.X + screen.X + 30), (int)(Dungeon.Torch.Y + screen.Y + 20));
        var behindWall = LitGame.Brightness(image, (int)(11.5f * Dungeon.Cell + screen.X), (int)(4 * Dungeon.Cell + screen.Y));
        Assert.True(nearTorch > 0.55f, $"Near the torch: {nearTorch}");
        Assert.True(behindWall < nearTorch * 0.5f, $"Behind the wall: {behindWall}, near the torch {nearTorch}");

        var hud = Dungeon.Hud;
        Assert.Equal(1f, LitGame.Brightness(image, (int)(hud.X + screen.X + 20), (int)(hud.Y + screen.Y + 5)));
    }

    [Fact]
    public async Task FramesCarryLightsTileOccludersCastersAndGlow()
    {
        await using var game = new LitGame(new NullRenderer(), Dungeon.Build);
        var lighting = game.Tick().Lighting;

        Assert.True(lighting.IsActive);
        Assert.Equal(2, lighting.Lights.Length);
        Assert.Equal(1, lighting.EmissiveBatches.Length);
        Assert.Contains(lighting.Occluders.ToArray(), o => o.EdgeCount == 4 && o.SelfShadows == false && o.Layers == 1);
        // The walls form the outer ring and three inner pieces across four chunks; merging keeps the edge count small.
        Assert.InRange(lighting.Edges.Length, 20, 80);
    }

    [Fact]
    public async Task ScenesWithoutLightsAreLeftUnlit()
    {
        await using var game = new LitGame(new NullRenderer(), (world, renderer) =>
        {
            var sprite = world.Create();
            world.Set(sprite, new Transform(Vector2.Zero));
            world.Set(sprite, new Sprite(renderer.WhiteTexture));
            world.Set(sprite, new ShadowCaster2D());
        });

        Assert.False(game.Tick().Lighting.IsActive);
        game.Environment.AmbientIntensity = 0.5f;
        Assert.True(game.Tick().Lighting.IsActive);
        game.Environment.Enabled = false;
        Assert.False(game.Tick().Lighting.IsEnabled);
    }

    [Fact]
    public async Task QualityPresetsCapLightsAndShadows()
    {
        await using var game = new LitGame(new NullRenderer(), (world, _) =>
        {
            for (var i = 0; i < 40; i++)
            {
                var light = world.Create();
                world.Set(light, new Transform(new Vector2(i * 10, 0)));
                world.Set(light, new Light2D { CastsShadows = true, Blend = i % 3 == 0 ? LightBlend.Multiply : LightBlend.Additive });
            }
        });

        var lights = game.Tick().Lighting.Lights.ToArray();
        Assert.Equal(LightingQualitySettings.Medium.MaxLights, lights.Length);
        Assert.Equal(LightingQualitySettings.Medium.MaxShadowedLights, lights.Count(l => l.CastsShadows));
        Assert.Equal(lights.OrderBy(l => l.Blend).Select(l => l.Blend), lights.Select(l => l.Blend));

        game.Environment.Quality = LightingQuality.Low;
        Assert.Equal(LightingQualitySettings.Low.MaxLights, game.Tick().Lighting.Lights.Length);
    }

    [Fact]
    public async Task LightsOutsideTheCameraAreCulled()
    {
        await using var game = new LitGame(new NullRenderer(), (world, _) =>
        {
            var near = world.Create();
            world.Set(near, new Transform(Vector2.Zero));
            world.Set(near, new Light2D());
            var far = world.Create();
            world.Set(far, new Transform(new Vector2(5000, 0)));
            world.Set(far, new Light2D());
            var sun = world.Create();
            world.Set(sun, new Transform(new Vector2(-9000, 0)));
            world.Set(sun, new Light2D { Type = LightType.Directional });
        });

        var lights = game.Tick().Lighting.Lights.ToArray();
        Assert.Equal(2, lights.Length);
        Assert.Contains(lights, l => l.Type == LightType.Directional);
    }

    [Theory]
    [InlineData(ExecutionModes.Edit, false)]
    [InlineData(ExecutionModes.Preview, true)]
    [InlineData(ExecutionModes.Play, true)]
    public async Task LightsFlickerInPreviewAndPlayOnly(ExecutionModes mode, bool animates)
    {
        await using var game = new LitGame(new NullRenderer(), (world, _) => Dungeon.AddTorch(world, Vector2.Zero, Color.White, 1), mode);
        var intensities = Enumerable.Range(0, 20).Select(_ => game.Tick().Lighting.Lights[0].Color.X).Distinct().Count();

        Assert.Equal(animates, intensities > 1);
    }

    [Fact]
    public async Task CollectingLightsDoesNotAllocateOnceWarm()
    {
        await using var game = new LitGame(new NullRenderer(), Dungeon.Build);
        for (var i = 0; i < 30; i++)
            game.Tick();

        // Steady-state allocations show in every round; a one-off, such as the runtime's own work, in only one.
        var fewest = double.MaxValue;
        for (var round = 0; round < 5 && fewest > 0; round++)
        {
            for (var i = 0; i < 60; i++)
                game.Game.Tick(1.0 / 60);

            var markers = game.Game.Profilers.Game.GetStatistics(60).Markers;
            var lighting = markers.Where(m => m.Name is "Systems/LightingSystem" or "Systems/LightAnimationSystem").ToList();
            Assert.Equal(2, lighting.Count);
            fewest = Math.Min(fewest, lighting.Sum(marker => marker.AverageAllocatedBytes));
        }

        Assert.Equal(0, fewest);
    }

    private static Vector2 ScreenOf(LitGame game)
    {
        foreach (var archetype in game.World.Query<Camera>())
            return new Vector2(LitGame.Width, LitGame.Height) / 2 - archetype.GetSpan<Camera>()[0].View.Position;
        return Vector2.Zero;
    }

    private static IRenderer CreateRenderer(string backend) => backend == "vulkan" ? VulkanRenderer.Create() : new SkiaRenderer();
}
