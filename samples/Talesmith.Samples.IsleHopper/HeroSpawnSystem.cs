using System.Numerics;
using Talesmith.Assets;
using Talesmith.Assets.Textures;
using Talesmith.Ecs;
using Talesmith.Rendering;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Rendering;
using Talesmith.Systems;

namespace Talesmith.Samples.IsleHopper;

/// <summary>Once the level is loaded, places the hero on its spawn point, gives the level's objects their behaviour and points the camera.</summary>
/// <remarks>
/// Map objects are recognised by their type in Hexy: "gem" objects bob and can be collected, "crab" objects walk and can be stomped,
/// and "checkpoint" objects become respawn points. Objects that move leave the map entity's hierarchy, which would otherwise hold them
/// in place. The camera is kept inside the level, not the larger backdrop map behind it.
/// </remarks>
[UpdateIn(SystemPhase.PreUpdate)]
public sealed class HeroSpawnSystem(IAssetManager assets, TextureCache textures, Course course, IsleHud hud) : ISystem
{
    public void Update(in SystemContext context)
    {
        var world = context.World;
        if (course.Level is not null || Level.Find(world) is not { } level)
            return;

        course.Level = level;
        var cellHeight = level.Layout.CellSize.Y;
        var spawn = level.FindObject(Level.SpawnType)!;
        var feet = level.Origin + spawn.Position + new Vector2(0, cellHeight / 2);
        var animations = new HeroAnimations(textures.Get(assets.Load<TextureAsset>(HeroAnimations.SheetPath)));

        var hero = context.Commands.Create();
        context.Commands.Set(hero, new Transform(feet));
        context.Commands.Set(hero, new Hero { Respawn = feet });
        context.Commands.Set(hero, new HeroControls());
        context.Commands.Set(hero, new SmoothMotion(feet));
        context.Commands.Set(hero, new Name("Hero"));
        context.Commands.Set(hero, new Sprite(animations.Sheet)
        {
            Source = animations.Idle.Frames[0].Source,
            Size = new Vector2(HeroAnimations.FrameWidth, HeroAnimations.FrameHeight),
            Origin = new Vector2(0.5f, HeroAnimations.FeetY),
            Layer = RenderLayers.Entities + 10
        });
        context.Commands.Set(hero, new SpriteAnimation(animations.Idle));
        context.Commands.Set(hero, animations);
        course.Hero = hero;

        var focus = context.Commands.Create();
        var focusPosition = feet + new Vector2(CameraFocusSystem.Ahead, -CameraFocusSystem.Above);
        context.Commands.Set(focus, new Transform(focusPosition));
        context.Commands.Set(focus, new Name("Camera focus"));
        course.CameraFocus = focus;

        var gems = 0;
        var random = new Random(1);
        foreach (var archetype in world.Query<MapObjectComponent, Transform>())
        {
            var entities = archetype.Entities;
            var objects = archetype.GetSpan<MapObjectComponent>();
            var transforms = archetype.GetSpan<Transform>();
            for (var i = 0; i < entities.Length; i++)
            {
                if (objects[i].Map != level.Entity)
                    continue;
                var mapObject = objects[i].Object;
                if (mapObject.Type is "gem" or "crab")
                {
                    context.Commands.Remove<Parent>(entities[i]);
                    context.Commands.Remove<LocalTransform>(entities[i]);
                }

                switch (mapObject.Type)
                {
                    case "gem":
                        gems++;
                        context.Commands.Set(entities[i], new Bob { BaseY = transforms[i].Position.Y, Phase = random.NextSingle() * MathF.Tau });
                        context.Commands.Set(entities[i], new SmoothMotion(transforms[i].Position));
                        break;
                    case "crab":
                        context.Commands.Set(entities[i], new Crab { Speed = mapObject.Properties.GetFloat("speed", 70), Direction = -1, Stomped = -1 });
                        context.Commands.Set(entities[i], new SmoothMotion(transforms[i].Position));
                        break;
                    case "checkpoint":
                        context.Commands.Set(entities[i], new Checkpoint());
                        break;
                }
            }
        }

        course.GemsTotal = gems;
        hud.Reset(level.Map.Properties.GetString("title") ?? Path.GetFileNameWithoutExtension(level.Map.Path), gems);

        if (world.Query<Camera>().TryGetSingle(out var cameraEntity))
        {
            ref var camera = ref world.Get<Camera>(cameraEntity);
            camera.Target = focus;
            camera.FollowSharpness = 7;
            camera.Bounds = level.Bounds;
            camera.View.Position = focusPosition;
        }
    }
}
