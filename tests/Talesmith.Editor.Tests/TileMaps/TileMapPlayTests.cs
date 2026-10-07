using System.Diagnostics;
using Talesmith.Assets.Maps;
using Talesmith.Editor.PlayMode;
using Talesmith.Editor.TileMaps.Tools;
using Talesmith.Grids;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Scenes;

namespace Talesmith.Editor.Tests.TileMaps;

public sealed class TileMapPlayTests
{
    private static readonly GridCoord[] Painted = [new(-9, -9), new(-8, -9), new(-7, -9)];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlayRunsUnsavedMapEditsWithoutSharingTheEditMap(bool fromStartScene) => Headless.Run(async () =>
    {
        await using var harness = await TileMapHarness.OpenAsync("platformer");
        var map = harness.Map;
        var layerIndex = map.IndexOf(harness.Layer);
        var tile = harness.Tile(2);
        harness.Editor.Brush.Pick(tile);
        harness.Drag(harness.Tool<BrushTool>(), Painted);
        Assert.True(harness.Editor.Maps.IsDirty(map));
        var editCells = harness.TilesAt(Painted);
        var editVersion = map.Version;

        var play = harness.Fixture.Get<IPlayModeService>();
        await (fromStartScene ? play.StartAsync() : play.PlayAsync());
        Assert.Equal(PlayState.Playing, play.State);
        var played = await WaitForMapAsync(play);

        var (same, cells) = await play.InvokeAsync(_ =>
        {
            var layer = (TileLayer)played.Layers[layerIndex];
            var result = Painted.Select(layer.GetCell).ToArray();
            foreach (var cell in Painted)
                layer.SetCell(cell, TileCell.Empty);
            return (ReferenceEquals(played, map), result);
        });
        await play.StopAsync();

        Assert.False(same);
        Assert.Equal(editCells, cells);
        Assert.All(cells, cell => Assert.Equal(tile, cell));
        Assert.Equal(editCells, harness.TilesAt(Painted));
        Assert.Equal(editVersion, map.Version);
        Assert.True(harness.Editor.Maps.IsDirty(map));
    });

    [Fact]
    public void PlayLoadsSavedMapsFromTheirFiles() => Headless.Run(async () =>
    {
        await using var harness = await TileMapHarness.OpenAsync("platformer");
        var map = harness.Map;
        var layerIndex = map.IndexOf(harness.Layer);
        var tile = harness.Tile(2);
        harness.Editor.Brush.Pick(tile);
        harness.Drag(harness.Tool<BrushTool>(), Painted);
        Assert.True(await harness.Editor.Maps.SaveAsync(map));

        var play = harness.Fixture.Get<IPlayModeService>();
        await play.PlayAsync();
        var played = await WaitForMapAsync(play);
        var cells = await play.InvokeAsync(_ => Painted.Select(((TileLayer)played.Layers[layerIndex]).GetCell).ToArray());
        await play.StopAsync();

        Assert.All(cells, cell => Assert.Equal(tile, cell));
    });

    private static async Task<TileMap> WaitForMapAsync(IPlayModeService play)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var map = await play.InvokeAsync(game =>
            {
                if (game.Scenes.Current is not DocumentScene { World: { } world } || game.Scenes.IsLoading)
                    return null;
                foreach (var archetype in world.Query<TileMapComponent>())
                {
                    if (archetype.GetSpan<TileMapComponent>() is [var first, ..])
                        return first.Map;
                }

                return null;
            });
            if (map is not null)
                return map;
            if (clock.Elapsed > TimeSpan.FromSeconds(30))
                throw new TimeoutException("The play session did not load the map.");
            await Task.Delay(10);
        }
    }
}
