using System.Numerics;
using Talesmith.Ecs;

namespace Talesmith.Physics.Tests;

public sealed class SolverTests
{
    [Fact]
    public void BodiesFallUnderGravity()
    {
        var test = new PhysicsTestWorld();
        var ball = test.Dynamic(new Vector2(0, -1000), Collider2D.Circle(10));

        test.Step(60);

        // Semi-implicit Euler over one second at 980 units/s² covers about 490 units.
        Assert.InRange(test.Position(ball).Y, -1000 + 480, -1000 + 500);
        Assert.InRange(test.Body(ball).Velocity.Y, 975, 985);
    }

    [Fact]
    public void AStackOfBoxesComesToRestWithoutJitter()
    {
        var test = new PhysicsTestWorld();
        test.Ground();
        var boxes = new Entity[8];
        for (var i = 0; i < boxes.Length; i++)
            boxes[i] = test.Dynamic(new Vector2(i % 2 == 0 ? 1.5f : -1.5f, -16 - i * 32.5f), Collider2D.Box(new Vector2(32, 32)), new Rigidbody2D { CanSleep = false });

        test.Step(240);
        var settled = boxes.Select(test.Position).ToArray();
        test.Step(120);

        for (var i = 0; i < boxes.Length; i++)
        {
            var position = test.Position(boxes[i]);
            Assert.InRange(Vector2.Distance(position, settled[i]), 0, 0.05f);
            Assert.InRange(MathF.Abs(position.X), 0.5f, 2.5f);
            Assert.InRange(test.Body(boxes[i]).Velocity.Length(), 0, 1f);
            Assert.InRange(position.Y, -16 - i * 32 - 1, -16 - i * 32 + (i + 1) * test.Physics.Settings.LinearSlop + 0.5f);
        }
    }

    [Fact]
    public void APyramidStaysStanding()
    {
        var test = new PhysicsTestWorld();
        test.Ground();
        var boxes = new List<Entity>();
        const int rows = 10;
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < rows - row; column++)
            {
                var x = (column - (rows - row - 1) / 2f) * 33;
                boxes.Add(test.Dynamic(new Vector2(x, -16 - row * 32.5f), Collider2D.Box(new Vector2(32, 32))));
            }
        }

        var top = boxes[^1];
        test.Step(600);

        Assert.InRange(test.Position(top).X, -2f, 2f);
        Assert.InRange(test.Position(top).Y, -16 - (rows - 1) * 32 - 1, -16 - (rows - 1) * 32 + rows * test.Physics.Settings.LinearSlop + 1);
    }

    [Fact]
    public void RestingBodiesFallAsleepAndWakeWhenPushed()
    {
        var test = new PhysicsTestWorld();
        test.Ground();
        var box = test.Dynamic(new Vector2(0, -16), Collider2D.Box(new Vector2(32, 32)));

        test.Step(120);
        Assert.False(test.Physics.IsAwake(box));

        test.Physics.AddImpulse(box, new Vector2(300, 0));
        Assert.True(test.Physics.IsAwake(box));
        test.Step();
        Assert.True(test.Position(box).X > 0);
    }

    [Fact]
    public void SettingTheVelocityWakesASleepingBody()
    {
        var test = new PhysicsTestWorld();
        test.Ground();
        var box = test.Dynamic(new Vector2(0, -16), Collider2D.Box(new Vector2(32, 32)));
        test.Step(120);

        test.Body(box).Velocity = new Vector2(0, -300);
        test.Step();

        Assert.True(test.Physics.IsAwake(box));
        Assert.True(test.Position(box).Y < -17);
    }

    [Fact]
    public void FullRestitutionBouncesBackNearlyToTheStartingHeight()
    {
        var test = new PhysicsTestWorld();
        test.Static(new Vector2(0, 50), Collider2D.Box(new Vector2(400, 100)) with { Restitution = 1 });
        var ball = test.Dynamic(new Vector2(0, -300), Collider2D.Circle(10) with { Restitution = 1 });

        var highest = float.MaxValue;
        var bounced = false;
        for (var i = 0; i < 150; i++)
        {
            test.Step();
            if (test.Body(ball).Velocity.Y < 0)
                bounced = true;
            if (bounced)
                highest = MathF.Min(highest, test.Position(ball).Y);
        }

        Assert.True(bounced);
        Assert.InRange(highest, -310, -270);
    }

    [Fact]
    public void ZeroRestitutionDoesNotBounce()
    {
        var test = new PhysicsTestWorld();
        test.Ground();
        var ball = test.Dynamic(new Vector2(0, -300), Collider2D.Circle(10));

        var highestAfterLanding = float.MaxValue;
        var landed = false;
        for (var i = 0; i < 150; i++)
        {
            test.Step();
            landed |= test.Position(ball).Y > -12;
            if (landed)
                highestAfterLanding = MathF.Min(highestAfterLanding, test.Position(ball).Y);
        }

        Assert.True(landed);
        Assert.InRange(highestAfterLanding, -12, -9);
    }

    [Fact]
    public void FrictionStopsASlidingBox()
    {
        var test = new PhysicsTestWorld();
        test.Ground();
        var box = test.Dynamic(new Vector2(0, -16), Collider2D.Box(new Vector2(32, 32)) with { Friction = 0.5f }, new Rigidbody2D { Velocity = new Vector2(400, 0) });

        test.Step(120);

        // The combined friction of about 0.45 decelerates the box at about 440 units/s², so it stops after about 180 units.
        Assert.InRange(test.Body(box).Velocity.X, -1, 1);
        Assert.InRange(test.Position(box).X, 150, 230);
    }

    [Fact]
    public void WithoutFrictionASlidingBoxKeepsItsSpeed()
    {
        var test = new PhysicsTestWorld();
        test.Static(new Vector2(0, 50), Collider2D.Box(new Vector2(40000, 100)) with { Friction = 0 });
        var box = test.Dynamic(new Vector2(0, -16), Collider2D.Box(new Vector2(32, 32)) with { Friction = 0 }, new Rigidbody2D { Velocity = new Vector2(400, 0) });

        test.Step(60);

        Assert.InRange(test.Body(box).Velocity.X, 399, 401);
    }

    [Fact]
    public void FrictionHoldsABoxOnAGentleSlopeButNotOnASteepOne()
    {
        var gentle = SlideOnSlope(MathHelperDegrees(15));
        var steep = SlideOnSlope(MathHelperDegrees(45));

        Assert.InRange(gentle, 0, 2);
        Assert.True(steep > 50);
    }

    [Fact]
    public void ForcesAccelerateAndTorquesTurn()
    {
        var test = new PhysicsTestWorld(s => s.Gravity = Vector2.Zero);
        var box = test.Dynamic(Vector2.Zero, Collider2D.Box(new Vector2(32, 32)), new Rigidbody2D { Mass = 2 });

        for (var i = 0; i < 60; i++)
        {
            test.Physics.AddForce(box, new Vector2(100, 0));
            test.Physics.AddTorque(box, 1000);
            test.Step();
        }

        Assert.InRange(test.Body(box).Velocity.X, 49.5f, 50.5f);
        Assert.True(test.Body(box).AngularVelocity > 0);
    }

    [Fact]
    public void FixedRotationKeepsBodiesUpright()
    {
        var test = new PhysicsTestWorld();
        test.Ground();
        var box = test.Dynamic(new Vector2(0, -40), Collider2D.Box(new Vector2(32, 32)), new Rigidbody2D { FixedRotation = true, AngularVelocity = 5 });
        test.Physics.AddImpulseAtPosition(box, new Vector2(100, 0), new Vector2(0, -56));

        test.Step(60);

        Assert.Equal(0, test.World.Get<Runtime.Components.Transform>(box).Rotation);
    }

    [Fact]
    public void KinematicBodiesMoveByVelocityAndCarryWhatRestsOnThem()
    {
        var test = new PhysicsTestWorld();
        var platform = test.World.Create(new Runtime.Components.Transform(new Vector2(0, 10)), Collider2D.Box(new Vector2(200, 20)),
            new Rigidbody2D(BodyType.Kinematic) { Velocity = new Vector2(60, 0) });
        var box = test.Dynamic(new Vector2(0, -16), Collider2D.Box(new Vector2(32, 32)) with { Friction = 1 });

        test.Step(60);

        Assert.InRange(test.Position(platform).X, 59, 61);
        Assert.InRange(test.Position(box).X, 50, 61);
        Assert.InRange(test.Position(box).Y, -18, -14);
    }

    [Fact]
    public void FastBodiesDoNotTunnelThroughThinWalls()
    {
        var test = new PhysicsTestWorld(s => s.Gravity = Vector2.Zero);
        test.Static(new Vector2(500, 0), Collider2D.Box(new Vector2(4, 400)));
        var bullet = test.Dynamic(Vector2.Zero, Collider2D.Circle(4), new Rigidbody2D { Velocity = new Vector2(12000, 0) });

        test.Step(10);

        Assert.True(test.Position(bullet).X < 500);
    }

    [Fact]
    public void ContinuousBodiesDoNotTunnelThroughMovingBodies()
    {
        var test = new PhysicsTestWorld(s => s.Gravity = Vector2.Zero);
        var wall = test.Dynamic(new Vector2(500, 0), Collider2D.Box(new Vector2(4, 400)), new Rigidbody2D { Mass = 100 });
        var bullet = test.Dynamic(Vector2.Zero, Collider2D.Circle(4),
            new Rigidbody2D { Velocity = new Vector2(12000, 0), CollisionDetection = CollisionDetection.Continuous });

        test.Step(4);

        Assert.True(test.Position(bullet).X < test.Position(wall).X);
        Assert.True(test.Body(wall).Velocity.X > 0);
    }

    [Fact]
    public void LayersThatDoNotCollidePassThroughEachOther()
    {
        var test = new PhysicsTestWorld(s => s.SetLayerCollision(1, 2, false));
        test.Static(new Vector2(0, 50), Collider2D.Box(new Vector2(400, 100)) with { Layer = 2 });
        var ghost = test.Dynamic(new Vector2(0, -20), Collider2D.Circle(10) with { Layer = 1 });
        var solid = test.Dynamic(new Vector2(50, -20), Collider2D.Circle(10) with { Layer = 3 });

        test.Step(60);

        Assert.True(test.Position(ghost).Y > 100);
        Assert.InRange(test.Position(solid).Y, -12, -9);
    }

    [Fact]
    public void CollisionMasksFilterInBothDirections()
    {
        var test = new PhysicsTestWorld();
        test.Static(new Vector2(0, 50), Collider2D.Box(new Vector2(400, 100)) with { Layer = 4 });
        var ignoring = test.Dynamic(new Vector2(0, -20), Collider2D.Circle(10) with { CollisionMask = ~PhysicsLayers.Mask(4) });

        test.Step(60);

        Assert.True(test.Position(ignoring).Y > 100);
    }

    [Fact]
    public void TeleportingABodyMovesIt()
    {
        var test = new PhysicsTestWorld();
        test.Ground();
        var box = test.Dynamic(new Vector2(0, -16), Collider2D.Box(new Vector2(32, 32)));
        test.Step(10);

        test.World.Get<Runtime.Components.Transform>(box).Position = new Vector2(1000, -500);
        test.Step();

        Assert.InRange(test.Position(box).X, 999, 1001);
        Assert.InRange(test.Position(box).Y, -500, -495);
    }

    [Fact]
    public void RemovingAColliderStopsCollisions()
    {
        var test = new PhysicsTestWorld();
        var ground = test.Ground();
        var box = test.Dynamic(new Vector2(0, -16), Collider2D.Box(new Vector2(32, 32)));
        test.Step(30);

        test.World.Destroy(ground);
        test.Step(30);

        Assert.True(test.Position(box).Y > 50);
        Assert.Equal(1, test.Physics.BodyCount);
    }

    private static float SlideOnSlope(float angle)
    {
        var test = new PhysicsTestWorld();
        test.Static(Vector2.Zero, Collider2D.Box(new Vector2(2000, 40)) with { Friction = 0.6f }, angle);
        var normal = new Vector2(MathF.Sin(angle), -MathF.Cos(angle));
        var start = normal * 37f;
        var box = test.World.Create(new Runtime.Components.Transform(start, angle), Collider2D.Box(new Vector2(32, 32)) with { Friction = 0.6f }, new Rigidbody2D());

        test.Step(120);

        return Vector2.Distance(test.Position(box), start);
    }

    private static float MathHelperDegrees(float degrees) => degrees * MathF.PI / 180;
}
