using System.Numerics;
using Talesmith.Assets;
using Talesmith.Assets.Maps;
using Talesmith.Audio;
using Talesmith.Ecs;
using Talesmith.Grids;
using Talesmith.Input;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Hosting;
using Talesmith.Runtime.Rendering;
using Talesmith.Runtime.Tweens;
using Talesmith.Systems;

namespace Talesmith.Samples.HexQuest;

/// <summary>Moves the hero from hex to hex with the keyboard or along a path to a clicked hex.</summary>
public sealed class HeroMovementSystem(IInputService input, PlayerControl control, RenderContext render, IAssetManager assets, IAudioService audio, QuestHud hud)
    : ISystem
{
    public const float StepSeconds = 0.22f;
    public const string StepSoundPath = "audio/step.wav";

    private readonly List<GridCoord> _path = [];
    private GridPathfinder? _pathfinder;
    private SoundClip? _stepSound;

    public void Update(in SystemContext context)
    {
        var world = context.World;
        if (!world.Query<Hero>().TryGetSingle(out var heroEntity) || !world.Query<TileMapComponent>().TryGetSingle(out var mapEntity))
            return;

        var map = world.Get<TileMapComponent>(mapEntity).Map;
        ref var hero = ref world.Get<Hero>(heroEntity);
        ref var transform = ref world.Get<Transform>(heroEntity);
        var path = world.Get<HeroPath>(heroEntity);

        if (hero.IsStepping)
            Advance(ref hero, ref transform, context.Time.DeltaTime);
        if (!hero.IsStepping && !control.IsSuspended)
            ChooseNextStep(map, ref hero, path);

        UpdateAnimation(world, heroEntity, hero);
        hud.Cell = hero.Cell;
        hud.Terrain = TerrainName(map, hero.Cell);
    }

    private void Advance(ref Hero hero, ref Transform transform, float deltaTime)
    {
        hero.StepProgress = Math.Min(1, hero.StepProgress + deltaTime / StepSeconds);
        transform.Position = Vector2.Lerp(hero.StepFrom, hero.StepTo, Easing.QuadInOut(hero.StepProgress));
        if (hero.StepProgress < 1)
            return;

        hero.Cell = hero.NextCell;
        hero.IsStepping = false;
        _stepSound ??= assets.Load<SoundClip>(StepSoundPath);
        audio.Play(_stepSound, new SoundOptions(Volume: 0.35f, Pitch: 0.9f + Random.Shared.NextSingle() * 0.2f));
    }

    private void ChooseNextStep(TileMap map, ref Hero hero, HeroPath path)
    {
        if (input.WasPressed(MouseButton.Left) && render.View.Contains(input.MousePosition))
            PlanPath(map, hero.Cell, map.Layout.WorldToCell(render.ScreenToWorld(input.MousePosition)), path);

        var move = input.Actions.TryGet("Move", out var action) ? action!.Vector : Vector2.Zero;
        if (move != Vector2.Zero)
        {
            path.Cells.Clear();
            var target = BestNeighbor(map.Layout, hero.Cell, move);
            if (Walkability.CanEnter(map, target))
                StartStep(map, ref hero, target);
            return;
        }

        if (path.Cells.Count > 0)
        {
            var next = path.Cells[0];
            path.Cells.RemoveAt(0);
            StartStep(map, ref hero, next);
        }
    }

    private void PlanPath(TileMap map, GridCoord from, GridCoord to, HeroPath path)
    {
        _pathfinder ??= new GridPathfinder(map.Layout);
        path.Cells.Clear();
        if (from == to || !_pathfinder.TryFindPath(from, to, Walkability.CostOn(map), _path, maxVisited: 4000))
            return;
        path.Cells.AddRange(_path.Skip(1));
    }

    private static void StartStep(TileMap map, ref Hero hero, GridCoord next)
    {
        hero.NextCell = next;
        hero.StepFrom = map.Layout.CellToWorld(hero.Cell);
        hero.StepTo = map.Layout.CellToWorld(next);
        hero.StepProgress = 0;
        hero.IsStepping = true;
    }

    private static GridCoord BestNeighbor(IGridLayout layout, GridCoord cell, Vector2 direction)
    {
        var origin = layout.CellToWorld(cell);
        var best = cell;
        var bestDot = float.MinValue;
        foreach (var offset in layout.NeighborOffsets)
        {
            var neighbor = cell + offset;
            var dot = Vector2.Dot(Vector2.Normalize(layout.CellToWorld(neighbor) - origin), Vector2.Normalize(direction));
            if (dot > bestDot)
            {
                bestDot = dot;
                best = neighbor;
            }
        }

        return best;
    }

    private static void UpdateAnimation(World world, Entity heroEntity, in Hero hero)
    {
        var animations = world.Get<HeroAnimations>(heroEntity);
        ref var animation = ref world.Get<SpriteAnimation>(heroEntity);
        animation.Play(hero.IsStepping ? animations.Walk : animations.Idle);
        if (hero.IsStepping && MathF.Abs(hero.StepTo.X - hero.StepFrom.X) > 1)
            world.Get<Sprite>(heroEntity).FlipX = hero.StepTo.X < hero.StepFrom.X;
    }

    private static string TerrainName(TileMap map, GridCoord cell)
    {
        var tile = map.TopTileAt(cell);
        return tile.IsEmpty ? "Open sea" : map.FindTileset(tile.TilesetId)?.Find(tile.TileId)?.Name ?? "Unknown";
    }
}
