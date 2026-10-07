using System.Numerics;
using Talesmith.Ecs;
using Talesmith.Grids;
using Talesmith.Mathematics;
using Talesmith.Runtime.Components;
using Talesmith.Systems;

namespace Talesmith.Samples.IsleHopper;

/// <summary>Runs and jumps the hero through the level's tiles, and sends it back to the last checkpoint when it touches a hazard or falls.</summary>
/// <remarks>
/// The hero is a box standing on its <see cref="Transform"/> position. It collides with "solid" tiles from every side and lands on
/// "oneWay" tiles from above. Movement runs at the game's fixed update rate, split into smaller steps so fast falls never pass through
/// a tile, so jumps reach the same height at any frame rate. Jumping forgives a late press after leaving a ledge and an early press
/// before landing, and releasing the button early makes a shorter jump. Input comes from <see cref="HeroControls"/>.
/// </remarks>
[UpdateIn(SystemPhase.FixedUpdate)]
public sealed class HeroMovementSystem(Course course) : ISystem
{
    public const float HalfWidth = 13;
    public const float Height = 54;
    public const float RunSpeed = 330;
    public const float GroundAcceleration = 2600;
    public const float GroundFriction = 3200;
    public const float AirAcceleration = 1700;
    public const float Gravity = 2300;
    public const float JumpSpeed = 860;
    public const float MaxFallSpeed = 950;
    public const float CoyoteSeconds = 0.1f;
    public const float JumpBufferSeconds = 0.12f;
    public const float StepSeconds = 1 / 120f;

    /// <summary>The upward speed kept when the jump button is released early, as a fraction of <see cref="JumpSpeed"/>.</summary>
    public const float ReleasedJumpSpeed = 0.4f;

    /// <summary>How far below the feet the hero looks for something to stand on.</summary>
    public const float GroundProbe = 1;

    /// <summary>How long the hero must have been in the air for landing to make a sound, so stepping down small edges stays quiet.</summary>
    public const float LandingSoundAirSeconds = 0.15f;

    public void Update(in SystemContext context)
    {
        var world = context.World;
        if (course.Level is not { } level || !world.IsAlive(course.Hero))
            return;

        ref var hero = ref world.Get<Hero>(course.Hero);
        ref var transform = ref world.Get<Transform>(course.Hero);
        ref var controls = ref world.Get<HeroControls>(course.Hero);
        var deltaTime = Math.Min(context.Time.DeltaTime, 0.05f);
        var run = controls.Run;

        hero.Invulnerable = Math.Max(0, hero.Invulnerable - deltaTime);
        hero.JumpBuffer = controls.JumpPressed ? JumpBufferSeconds : Math.Max(0, hero.JumpBuffer - deltaTime);
        controls.JumpPressed = false;
        hero.CoyoteTime = hero.OnGround ? CoyoteSeconds : Math.Max(0, hero.CoyoteTime - deltaTime);

        var acceleration = !hero.OnGround ? AirAcceleration : run != 0 ? GroundAcceleration : GroundFriction;
        hero.Velocity.X = MoveTowards(hero.Velocity.X, run * RunSpeed, acceleration * deltaTime);
        if (hero.JumpBuffer > 0 && hero.CoyoteTime > 0)
        {
            hero.Velocity.Y = -JumpSpeed;
            hero.JumpBuffer = 0;
            hero.CoyoteTime = 0;
            hero.OnGround = false;
            course.Play("jump", 0.35f, 0.95f + Random.Shared.NextSingle() * 0.1f);
        }

        if (!controls.JumpHeld && hero.Velocity.Y < -JumpSpeed * ReleasedJumpSpeed)
            hero.Velocity.Y = -JumpSpeed * ReleasedJumpSpeed;
        hero.Velocity.Y = Math.Min(hero.Velocity.Y + Gravity * deltaTime, MaxFallSpeed);

        hero.OnGround = false;
        var steps = Math.Max(1, (int)MathF.Ceiling(deltaTime / StepSeconds));
        for (var i = 0; i < steps; i++)
        {
            MoveHorizontally(level, ref hero, ref transform.Position, deltaTime / steps);
            MoveVertically(level, ref hero, ref transform.Position, deltaTime / steps);
        }

        if (!hero.OnGround && hero.Velocity.Y >= 0 && FindGround(level, transform.Position) is { } ground)
        {
            transform.Position.Y = ground;
            hero.OnGround = true;
            hero.Velocity.Y = 0;
        }

        if (hero.OnGround)
        {
            if (hero.AirTime > LandingSoundAirSeconds)
                course.Play("step", 0.3f, 0.8f);
            hero.AirTime = 0;
        }
        else
        {
            hero.AirTime += deltaTime;
        }
        if (TouchesHazard(level, transform.Position) || transform.Position.Y - Height > level.Bounds.Bottom)
            course.Hurt(ref hero, ref transform);

        Footsteps(ref hero, run, deltaTime);
        if (MathF.Abs(hero.Velocity.X) > 1)
            hero.FacingLeft = hero.Velocity.X < 0;
        Animate(world, course.Hero, hero);
    }

    public static Rect2 Bounds(Vector2 feet) => new(feet.X - HalfWidth, feet.Y - Height, HalfWidth * 2, Height);

    private static void MoveHorizontally(Level level, ref Hero hero, ref Vector2 position, float deltaTime)
    {
        if (hero.Velocity.X == 0)
            return;
        position.X += hero.Velocity.X * deltaTime;
        var (min, max) = level.CellsIn(Bounds(position));
        var column = hero.Velocity.X > 0 ? max.X : min.X;
        for (var row = min.Y; row <= max.Y; row++)
        {
            var cell = new GridCoord(column, row);
            if ((level.TraitsAt(cell) & TileTraits.Solid) == 0)
                continue;
            var tile = level.CellRect(cell);
            position.X = hero.Velocity.X > 0 ? tile.Left - HalfWidth : tile.Right + HalfWidth;
            hero.Velocity.X = 0;
            return;
        }
    }

    private static void MoveVertically(Level level, ref Hero hero, ref Vector2 position, float deltaTime)
    {
        var previousFeet = position.Y;
        position.Y += hero.Velocity.Y * deltaTime;
        var (min, max) = level.CellsIn(Bounds(position));
        var falling = hero.Velocity.Y >= 0;
        var row = falling ? max.Y : min.Y;
        for (var column = min.X; column <= max.X; column++)
        {
            var cell = new GridCoord(column, row);
            var traits = level.TraitsAt(cell);
            var tile = level.CellRect(cell);
            var lands = falling && ((traits & TileTraits.Solid) != 0 || ((traits & TileTraits.OneWay) != 0 && previousFeet <= tile.Top + 0.5f));
            if (lands)
            {
                position.Y = tile.Top;
                hero.Velocity.Y = 0;
                hero.OnGround = true;
                return;
            }

            if (!falling && (traits & TileTraits.Solid) != 0)
            {
                position.Y = tile.Bottom + Height;
                hero.Velocity.Y = 0;
                return;
            }
        }
    }

    /// <summary>Finds the top of a tile the hero can stand on within <see cref="GroundProbe"/> below its feet, however little it moved.</summary>
    /// <returns>The tile's top edge, or null when there is nothing to stand on.</returns>
    private static float? FindGround(Level level, Vector2 feet)
    {
        var (min, max) = level.CellsIn(new Rect2(feet.X - HalfWidth, feet.Y, HalfWidth * 2, GroundProbe));
        for (var y = min.Y; y <= max.Y; y++)
        {
            for (var x = min.X; x <= max.X; x++)
            {
                var cell = new GridCoord(x, y);
                var top = level.CellRect(cell).Top;
                if ((level.TraitsAt(cell) & (TileTraits.Solid | TileTraits.OneWay)) != 0 && top >= feet.Y - 0.01f && top <= feet.Y + GroundProbe)
                    return top;
            }
        }

        return null;
    }

    /// <summary>Whether the hero's body reaches into the lower part of a hazard cell, where the water or the spikes are.</summary>
    private static bool TouchesHazard(Level level, Vector2 feet)
    {
        var body = Bounds(feet);
        var inner = new Rect2(body.X + 4, body.Y + 8, body.Width - 8, body.Height - 10);
        var (min, max) = level.CellsIn(inner);
        for (var y = min.Y; y <= max.Y; y++)
        {
            for (var x = min.X; x <= max.X; x++)
            {
                var cell = new GridCoord(x, y);
                if ((level.TraitsAt(cell) & TileTraits.Hazard) == 0)
                    continue;
                var tile = level.CellRect(cell);
                var danger = Rect2.FromEdges(tile.Left, tile.Top + tile.Height * 0.45f, tile.Right, tile.Bottom);
                if (danger.Intersects(inner))
                    return true;
            }
        }

        return false;
    }

    private void Footsteps(ref Hero hero, float run, float deltaTime)
    {
        if (!hero.OnGround || run == 0 || MathF.Abs(hero.Velocity.X) < RunSpeed * 0.5f)
        {
            hero.StepTimer = 0;
            return;
        }

        hero.StepTimer -= deltaTime;
        if (hero.StepTimer > 0)
            return;
        hero.StepTimer = 0.3f;
        course.Play("step", 0.2f, 0.9f + Random.Shared.NextSingle() * 0.25f);
    }

    private static void Animate(World world, Entity entity, in Hero hero)
    {
        var animations = world.Get<HeroAnimations>(entity);
        ref var animation = ref world.Get<SpriteAnimation>(entity);
        animation.Play(!hero.OnGround ? hero.Velocity.Y < 0 ? animations.Jump : animations.Fall
            : MathF.Abs(hero.Velocity.X) > 20 ? animations.Run : animations.Idle);

        ref var sprite = ref world.Get<Sprite>(entity);
        sprite.FlipX = hero.FacingLeft;
        var blink = hero.Invulnerable > 0 && (int)(hero.Invulnerable * 12) % 2 == 0;
        sprite.Tint = Color.White.WithAlpha(blink ? (byte)90 : (byte)255);
    }

    private static float MoveTowards(float value, float target, float maxDelta) =>
        MathF.Abs(target - value) <= maxDelta ? target : value + MathF.Sign(target - value) * maxDelta;
}
