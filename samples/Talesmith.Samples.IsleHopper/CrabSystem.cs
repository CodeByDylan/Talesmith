using System.Numerics;
using Talesmith.Ecs;
using Talesmith.Mathematics;
using Talesmith.Runtime.Components;
using Talesmith.Systems;

namespace Talesmith.Samples.IsleHopper;

/// <summary>Walks crabs back and forth, turning at walls and ledges, and lets the hero stomp them or get hurt by them.</summary>
/// <remarks>Crabs are tile objects from the "Critters" tileset, whose tiles are two walking frames and a stomped frame.</remarks>
[UpdateIn(SystemPhase.FixedUpdate)]
[UpdateAfter(typeof(HeroMovementSystem))]
public sealed class CrabSystem(Course course) : ISystem
{
    public const float HalfWidth = 20;
    public const float Height = 28;
    public const float StompBounce = 560;
    public const float RemoveAfterSeconds = 0.6f;

    /// <summary>How far below the top of a crab the hero's feet may have been and still count as landing on it.</summary>
    private const float StompTolerance = 12;

    public void Update(in SystemContext context)
    {
        var world = context.World;
        if (course.Level is not { } level || !world.IsAlive(course.Hero))
            return;

        var deltaTime = Math.Min(context.Time.DeltaTime, 0.05f);
        ref var hero = ref world.Get<Hero>(course.Hero);
        ref var heroTransform = ref world.Get<Transform>(course.Hero);
        var heroBounds = HeroMovementSystem.Bounds(heroTransform.Position);
        var previousFeet = heroTransform.Position.Y - hero.Velocity.Y * deltaTime;

        foreach (var archetype in world.Query<Crab, Transform, Sprite, MapObjectComponent>())
        {
            var entities = archetype.Entities;
            var crabs = archetype.GetSpan<Crab>();
            var transforms = archetype.GetSpan<Transform>();
            var sprites = archetype.GetSpan<Sprite>();
            var objects = archetype.GetSpan<MapObjectComponent>();
            for (var i = 0; i < entities.Length; i++)
            {
                ref var crab = ref crabs[i];
                ref var position = ref transforms[i].Position;
                var tileset = level.Map.FindTileset(objects[i].Object.Tile.TilesetId);
                if (crab.Stomped >= 0)
                {
                    crab.Stomped += deltaTime;
                    if (tileset is not null)
                        sprites[i].Source = tileset.SourceRect(2);
                    if (crab.Stomped > RemoveAfterSeconds)
                        context.Commands.Destroy(entities[i]);
                    continue;
                }

                Walk(level, ref crab, ref position, sprites[i].Size.Y / 2, deltaTime);
                crab.AnimationTime += deltaTime;
                if (tileset is not null)
                    sprites[i].Source = tileset.SourceRect((int)(crab.AnimationTime / 0.18f) % 2);

                var feet = position.Y + sprites[i].Size.Y / 2;
                var body = new Rect2(position.X - HalfWidth, feet - Height, HalfWidth * 2, Height);
                if (!body.Intersects(heroBounds) || !course.IsRunning)
                    continue;

                if (hero.Velocity.Y > 0 && previousFeet <= body.Top + StompTolerance)
                {
                    crab.Stomped = 0;
                    hero.Velocity.Y = -StompBounce;
                    course.Play("stomp", 0.6f);
                }
                else
                {
                    course.Hurt(ref hero, ref heroTransform);
                }
            }
        }
    }

    private static void Walk(Level level, ref Crab crab, ref Vector2 position, float halfHeight, float deltaTime)
    {
        var feet = position.Y + halfHeight;
        var ahead = position.X + crab.Direction * (HalfWidth + 2);
        var wall = (level.TraitsAt(level.CellAt(new Vector2(ahead, feet - 16))) & TileTraits.Solid) != 0;
        var floor = (level.TraitsAt(level.CellAt(new Vector2(ahead, feet + 8))) & (TileTraits.Solid | TileTraits.OneWay)) != 0;
        if (wall || !floor)
            crab.Direction = -crab.Direction;
        position.X += crab.Direction * crab.Speed * deltaTime;
    }
}
