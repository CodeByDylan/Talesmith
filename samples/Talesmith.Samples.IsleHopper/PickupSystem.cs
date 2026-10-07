using System.Numerics;
using Talesmith.Runtime.Components;
using Talesmith.Systems;

namespace Talesmith.Samples.IsleHopper;

/// <summary>Handles what the hero touches besides crabs: emeralds, checkpoint flags, signs and the lighthouse at the end.</summary>
[UpdateIn(SystemPhase.FixedUpdate)]
[UpdateAfter(typeof(HeroMovementSystem))]
public sealed class PickupSystem(Course course, IsleHud hud) : ISystem
{
    public const float GemReach = 36;
    public const float BobHeight = 5;

    public void Update(in SystemContext context)
    {
        var world = context.World;
        if (course.Level is not { } level || !world.IsAlive(course.Hero))
            return;

        ref var hero = ref world.Get<Hero>(course.Hero);
        var feet = world.Get<Transform>(course.Hero).Position;
        var center = feet - new Vector2(0, HeroMovementSystem.Height / 2);
        var time = (float)context.Time.TotalTime;

        foreach (var archetype in world.Query<Bob, Transform>())
        {
            var entities = archetype.Entities;
            var bobs = archetype.GetSpan<Bob>();
            var transforms = archetype.GetSpan<Transform>();
            for (var i = 0; i < entities.Length; i++)
            {
                ref var position = ref transforms[i].Position;
                position.Y = bobs[i].BaseY + MathF.Sin(time * 3 + bobs[i].Phase) * BobHeight;
                if (course.IsRunning && Vector2.Distance(position, center) < GemReach)
                {
                    context.Commands.Destroy(entities[i]);
                    course.CollectGem();
                }
            }
        }

        foreach (var archetype in world.Query<Checkpoint, Transform, Sprite, MapObjectComponent>())
        {
            var checkpoints = archetype.GetSpan<Checkpoint>();
            var transforms = archetype.GetSpan<Transform>();
            var sprites = archetype.GetSpan<Sprite>();
            var objects = archetype.GetSpan<MapObjectComponent>();
            for (var i = 0; i < checkpoints.Length; i++)
            {
                var flag = transforms[i].Position;
                var flagFeet = flag.Y + sprites[i].Size.Y / 2;
                if (checkpoints[i].Reached || MathF.Abs(feet.X - flag.X) > 28 || MathF.Abs(feet.Y - flagFeet) > 70)
                    continue;
                checkpoints[i].Reached = true;
                hero.Respawn = new Vector2(flag.X, flagFeet);
                if (level.Map.FindTileset(objects[i].Object.Tile.TilesetId) is { } tileset)
                    sprites[i].Source = tileset.SourceRect(1);
                course.Play("checkpoint", 0.5f);
            }
        }

        string? hint = null;
        foreach (var archetype in world.Query<MapObjectComponent, Transform>())
        {
            var objects = archetype.GetSpan<MapObjectComponent>();
            var transforms = archetype.GetSpan<Transform>();
            for (var i = 0; i < objects.Length; i++)
            {
                var mapObject = objects[i].Object;
                var position = transforms[i].Position;
                if (mapObject.Type == "sign" && MathF.Abs(feet.X - position.X) < 56 && MathF.Abs(feet.Y - position.Y) < 72)
                    hint = mapObject.Properties.GetString("text");
                else if (mapObject.Type == "goal" && MathF.Abs(feet.X - position.X) < 40 && feet.Y > position.Y && feet.Y < position.Y + 140)
                    course.Complete();
            }
        }

        hud.Hint = hint;
    }
}
