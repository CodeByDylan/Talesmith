using System.Numerics;
using Talesmith.Ecs;
using Talesmith.Runtime.Components;

namespace Talesmith.Physics.Tests;

public sealed class CharacterControllerTests
{
    private const float Gravity = 2000;

    [Fact]
    public void ACharacterFallsOntoTheGroundAndIsGrounded()
    {
        var test = new PhysicsTestWorld();
        test.Ground();
        var hero = Hero(test, new Vector2(0, -100));

        var velocity = Fall(test, hero, 60);

        ref var controller = ref test.World.Get<CharacterController2D>(hero);
        Assert.True(controller.IsGrounded);
        Assert.Equal(0, velocity.Y);
        Assert.InRange(test.Position(hero).Y, -21, -20.4f);
        Assert.Equal(-1, controller.GroundNormal.Y, 3);
        Assert.False(controller.Ground.IsNull);
    }

    [Fact]
    public void StandingStillOnTheGroundStaysGrounded()
    {
        var test = new PhysicsTestWorld();
        test.Ground();
        var hero = Hero(test, new Vector2(0, -20.5f));

        var collisions = test.Physics.MoveCharacter(hero, Vector2.Zero);

        Assert.Equal(CharacterCollisions.Below, collisions);
        Assert.Equal(new Vector2(0, -20.5f), test.Position(hero));
    }

    [Fact]
    public void WallsStopHorizontalMovementAndSetSides()
    {
        var test = new PhysicsTestWorld();
        test.Ground();
        test.Static(new Vector2(100, -50), Collider2D.Box(new Vector2(20, 100)));
        var hero = Hero(test, new Vector2(0, -20.5f));

        CharacterCollisions collisions = default;
        for (var i = 0; i < 30; i++)
            collisions = test.Physics.MoveCharacter(hero, new Vector2(8, 4));

        Assert.True(collisions.HasFlag(CharacterCollisions.Sides));
        Assert.True(collisions.HasFlag(CharacterCollisions.Below));
        Assert.InRange(test.Position(hero).X, 79, 80);
        Assert.InRange(test.Position(hero).Y, -21, -20.4f);
    }

    [Fact]
    public void CeilingsStopJumps()
    {
        var test = new PhysicsTestWorld();
        test.Ground();
        test.Static(new Vector2(0, -100), Collider2D.Box(new Vector2(200, 20)));
        var hero = Hero(test, new Vector2(0, -20.5f));

        var collisions = test.Physics.MoveCharacter(hero, new Vector2(0, -100));

        Assert.Equal(CharacterCollisions.Above, collisions);
        Assert.InRange(test.Position(hero).Y, -70.5f, -69.5f);
    }

    [Fact]
    public void WalkableSlopesAreClimbedAndSteepOnesBlock()
    {
        var gentle = WalkUpRamp(20);
        var steep = WalkUpRamp(70);

        Assert.True(gentle.X > 300, $"Walked to {gentle} up a gentle slope.");
        Assert.True(gentle.Y < -70, $"Walked to {gentle} up a gentle slope.");
        Assert.True(steep.X < 100, $"Walked to {steep} up a steep slope.");
    }

    [Fact]
    public void WalkingDownASlopeStaysGrounded()
    {
        var test = new PhysicsTestWorld();
        var angle = 30 * MathF.PI / 180;
        test.Static(Vector2.Zero, Collider2D.Box(new Vector2(2000, 40)), angle);
        var normal = new Vector2(MathF.Sin(angle), -MathF.Cos(angle));
        var hero = Hero(test, normal * 45f);
        Fall(test, hero, 20);

        var grounded = 0;
        for (var i = 0; i < 60; i++)
        {
            test.Physics.MoveCharacter(hero, new Vector2(5, 0));
            if (test.World.Get<CharacterController2D>(hero).IsGrounded)
                grounded++;
        }

        Assert.Equal(60, grounded);
    }

    [Fact]
    public void StepsUpToTheStepOffsetAreClimbedAndHigherOnesBlock()
    {
        var test = new PhysicsTestWorld();
        test.Ground();
        test.Static(new Vector2(150, -5), Collider2D.Box(new Vector2(100, 10)));
        test.Static(new Vector2(350, -15), Collider2D.Box(new Vector2(100, 30)));
        var hero = Hero(test, new Vector2(0, -20.5f), stepOffset: 12);

        for (var i = 0; i < 30; i++)
            test.Physics.MoveCharacter(hero, new Vector2(5, 2));
        Assert.InRange(test.Position(hero).X, 145, 151);
        Assert.InRange(test.Position(hero).Y, -31, -30);

        for (var i = 0; i < 60; i++)
            test.Physics.MoveCharacter(hero, new Vector2(5, 2));
        Assert.InRange(test.Position(hero).X, 289, 290);
        Assert.InRange(test.Position(hero).Y, -21, -20);
    }

    [Fact]
    public void OneWayPlatformsCarryFromAboveAndLetThroughFromBelow()
    {
        var test = new PhysicsTestWorld();
        test.Ground();
        test.Static(new Vector2(0, -100), Collider2D.Box(new Vector2(200, 10)) with { OneWay = true });
        var hero = Hero(test, new Vector2(0, -20.5f));

        test.Physics.MoveCharacter(hero, new Vector2(0, -150));
        Assert.InRange(test.Position(hero).Y, -171, -170);

        Fall(test, hero, 60);

        Assert.True(test.World.Get<CharacterController2D>(hero).IsGrounded);
        Assert.InRange(test.Position(hero).Y, -126, -125);
    }

    [Fact]
    public void DynamicBodiesBlockCharactersAndAppearInTheirContacts()
    {
        var test = new PhysicsTestWorld();
        test.Ground();
        var crate = test.Dynamic(new Vector2(100, -16), Collider2D.Box(new Vector2(32, 32)));
        var hero = Hero(test, new Vector2(0, -20.5f));
        test.Step(5);

        for (var i = 0; i < 40; i++)
        {
            test.Physics.MoveCharacter(hero, new Vector2(4, 0));
            test.Step();
            Assert.True(test.Position(hero).X < test.Position(crate).X - 25.9f, "The character never overlaps the crate.");
        }

        Span<ContactInfo> contacts = stackalloc ContactInfo[4];
        var count = test.Physics.GetContacts(hero, contacts);
        Assert.Contains(crate, contacts[..count].ToArray().Select(c => c.Other));
    }

    [Fact]
    public void CharactersLiftWhatRestsOnThem()
    {
        var test = new PhysicsTestWorld();
        test.Ground();
        var hero = Hero(test, new Vector2(0, -20.5f));
        var crate = test.Dynamic(new Vector2(0, -60), Collider2D.Box(new Vector2(20, 20)));
        test.Step(30);

        test.Physics.MoveCharacter(hero, new Vector2(0, -10));
        test.Step();

        Assert.True(test.Body(crate).Velocity.Y < 0, "The crate is lifted along with the character.");
    }

    [Fact]
    public void ACharacterSpawnedExactlyOnTheGroundWalksJumpsAndLands()
    {
        var test = new PhysicsTestWorld();
        test.Ground();
        var hero = Hero(test, new Vector2(0, -20));

        Assert.Equal(CharacterCollisions.Below, test.Physics.MoveCharacter(hero, new Vector2(0, 2)));
        test.Physics.MoveCharacter(hero, new Vector2(5, 0));
        Assert.InRange(test.Position(hero).X, 4.9f, 5.1f);

        test.Physics.MoveCharacter(hero, new Vector2(0, -30));
        Assert.InRange(test.Position(hero).Y, -51, -49);

        Fall(test, hero, 60);
        Assert.True(test.World.Get<CharacterController2D>(hero).IsGrounded);
        Assert.InRange(test.Position(hero).Y, -21, -19.9f);
    }

    [Fact]
    public void ACharacterExactlyTouchingAWallMovesAwayAndAlongIt()
    {
        var test = new PhysicsTestWorld();
        test.Static(new Vector2(100, -50), Collider2D.Box(new Vector2(20, 100)));
        var hero = Hero(test, new Vector2(80, -50));

        Assert.Equal(CharacterCollisions.Sides, test.Physics.MoveCharacter(hero, new Vector2(5, -5)));
        Assert.InRange(test.Position(hero).X, 79.4f, 80);
        Assert.InRange(test.Position(hero).Y, -55.1f, -54.9f);

        test.Physics.MoveCharacter(hero, new Vector2(-5, 0));
        Assert.InRange(test.Position(hero).X, 74, 75.1f);
    }

    [Fact]
    public void ACharacterExactlyTouchingACeilingMovesAlongAndAwayFromIt()
    {
        var test = new PhysicsTestWorld();
        test.Static(new Vector2(0, -100), Collider2D.Box(new Vector2(200, 20)));
        var hero = Hero(test, new Vector2(0, -70));

        Assert.Equal(CharacterCollisions.Above, test.Physics.MoveCharacter(hero, new Vector2(5, -5)));
        Assert.InRange(test.Position(hero).X, 4.9f, 5.1f);

        test.Physics.MoveCharacter(hero, new Vector2(0, 5));
        Assert.InRange(test.Position(hero).Y, -65.1f, -64.4f);
    }

    [Fact]
    public void ACharacterExactlyOnAOneWayPlatformStandsAndWalksOnIt()
    {
        var test = new PhysicsTestWorld();
        test.Static(new Vector2(0, -100), Collider2D.Box(new Vector2(200, 10)) with { OneWay = true });
        var hero = Hero(test, new Vector2(0, -125));

        Fall(test, hero, 30);
        Assert.True(test.World.Get<CharacterController2D>(hero).IsGrounded);
        Assert.InRange(test.Position(hero).Y, -126, -124.9f);

        test.Physics.MoveCharacter(hero, new Vector2(5, 0));
        Assert.InRange(test.Position(hero).X, 4.9f, 5.1f);
    }

    [Fact]
    public void MovingACharacterWithoutAControllerFails()
    {
        var test = new PhysicsTestWorld();
        var box = test.Dynamic(Vector2.Zero, Collider2D.Box(new Vector2(10, 10)));

        Assert.Throws<InvalidOperationException>(() => test.Physics.MoveCharacter(box, Vector2.One));
    }

    private static Entity Hero(PhysicsTestWorld test, Vector2 position, float stepOffset = 0)
    {
        var hero = test.World.Create(new Transform(position), Collider2D.Box(new Vector2(20, 40)), new CharacterController2D { StepOffset = stepOffset });
        test.Physics.BeginStep();
        return hero;
    }

    private static Vector2 Fall(PhysicsTestWorld test, Entity hero, int steps)
    {
        var velocity = Vector2.Zero;
        for (var i = 0; i < steps; i++)
        {
            velocity.Y += Gravity * PhysicsTestWorld.Dt;
            var collisions = test.Physics.MoveCharacter(hero, velocity * PhysicsTestWorld.Dt);
            if ((collisions & CharacterCollisions.Below) != 0)
                velocity.Y = 0;
        }

        return velocity;
    }

    /// <summary>Walks right for two seconds toward a wedge whose slope starts on the ground at x = 100.</summary>
    private static Vector2 WalkUpRamp(float degrees)
    {
        var test = new PhysicsTestWorld();
        test.Ground();
        var height = 600 * MathF.Tan(degrees * MathF.PI / 180);
        test.Static(new Vector2(100, 0), Collider2D.Polygon(Vector2.Zero, new Vector2(600, 0), new Vector2(600, -height)));
        var hero = Hero(test, new Vector2(0, -20.5f));

        var velocity = Vector2.Zero;
        for (var i = 0; i < 120; i++)
        {
            velocity.Y += Gravity * PhysicsTestWorld.Dt;
            var collisions = test.Physics.MoveCharacter(hero, new Vector2(5, velocity.Y * PhysicsTestWorld.Dt));
            if ((collisions & CharacterCollisions.Below) != 0)
                velocity.Y = 0;
        }

        return test.Position(hero);
    }
}
