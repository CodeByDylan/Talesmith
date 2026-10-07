using System.Numerics;
using Talesmith.Assets;
using Talesmith.Assets.Textures;
using Talesmith.Ecs;
using Talesmith.Rendering;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Rendering;
using Talesmith.Systems;

namespace Talesmith.Samples.HexQuest;

/// <summary>Places the hero on the map's spawn point whenever a map has no hero yet, and points the camera at it.</summary>
[UpdateIn(SystemPhase.PreUpdate)]
public sealed class HeroSpawnSystem(IAssetManager assets, TextureCache textures, QuestHud hud) : ISystem
{
    public const string SpawnType = "spawn";

    public void Update(in SystemContext context)
    {
        var world = context.World;
        if (!world.Query<Hero>().IsEmpty || !world.Query<TileMapComponent>().TryGetSingle(out var mapEntity))
            return;

        var map = world.Get<TileMapComponent>(mapEntity).Map;
        hud.MapTitle = map.Properties.GetString("title") ?? Path.GetFileNameWithoutExtension(map.Path);

        var spawn = map.ObjectLayers.SelectMany(l => l.Objects).FirstOrDefault(o => string.Equals(o.Type, SpawnType, StringComparison.OrdinalIgnoreCase));
        var cell = spawn?.Cell ?? default;
        var position = map.Layout.CellToWorld(cell);
        var animations = new HeroAnimations(textures.Get(assets.Load<TextureAsset>(HeroAnimations.SheetPath)));

        var hero = context.Commands.Create();
        context.Commands.Set(hero, new Transform(position));
        context.Commands.Set(hero, new Hero { Cell = cell, NextCell = cell });
        context.Commands.Set(hero, new Name("Hero"));
        context.Commands.Set(hero, new Sprite(animations.Sheet)
        {
            Source = animations.Idle.Frames[0].Source,
            Size = new Vector2(HeroAnimations.FrameWidth, HeroAnimations.FrameHeight),
            Origin = new Vector2(0.5f, 0.92f),
            SortByY = true,
            Layer = RenderLayers.Entities
        });
        context.Commands.Set(hero, new SpriteAnimation(animations.Idle));
        context.Commands.Set(hero, new TriggerActivator());
        context.Commands.Set(hero, new HeroPath());
        context.Commands.Set(hero, animations);

        if (world.Query<Camera>().TryGetSingle(out var cameraEntity))
        {
            ref var camera = ref world.Get<Camera>(cameraEntity);
            camera.Target = hero;
            camera.FollowSharpness = 6;
            camera.View.Position = position;
        }
    }
}
