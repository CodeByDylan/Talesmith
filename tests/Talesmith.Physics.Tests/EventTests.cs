using System.Numerics;

namespace Talesmith.Physics.Tests;

public sealed class EventTests
{
    [Fact]
    public void ALandingBodyEntersStaysAndExitsWhenLifted()
    {
        var test = new PhysicsTestWorld();
        var ground = test.Ground();
        var box = test.Dynamic(new Vector2(0, -40), Collider2D.Box(new Vector2(32, 32)), new Rigidbody2D { CanSleep = false });
        var log = Record(test);

        test.Step(60);
        Assert.Equal("enter", log[0].Kind);
        Assert.True(log[0].Contact.Involves(box) && log[0].Contact.Involves(ground));
        Assert.True(log[0].Contact.NormalImpulse > 0);
        Assert.Single(log, e => e.Kind == "enter");
        Assert.True(log.Count(e => e.Kind == "stay") > 10);
        Assert.DoesNotContain(log, e => e.Kind == "exit");

        log.Clear();
        test.World.Get<Runtime.Components.Transform>(box).Position = new Vector2(0, -500);
        test.Step();
        Assert.Equal("exit", Assert.Single(log).Kind);
    }

    [Fact]
    public void TheEnterNormalPointsFromTheFirstEntityToTheSecond()
    {
        var test = new PhysicsTestWorld();
        test.Ground();
        test.Dynamic(new Vector2(0, -40), Collider2D.Box(new Vector2(32, 32)));
        var log = Record(test);

        test.Step(60);

        var contact = log.First(e => e.Kind == "enter").Contact;
        var fromAToB = test.Position(contact.B) - test.Position(contact.A);
        Assert.True(Vector2.Dot(contact.Normal, fromAToB) > 0);
    }

    [Fact]
    public void DestroyingAnEntityEndsItsContacts()
    {
        var test = new PhysicsTestWorld();
        test.Ground();
        var box = test.Dynamic(new Vector2(0, -16), Collider2D.Box(new Vector2(32, 32)));
        var log = Record(test);
        test.Step(5);

        test.World.Destroy(box);
        test.Step();

        Assert.Equal("exit", log[^1].Kind);
        Assert.True(log[^1].Contact.Involves(box));
    }

    [Fact]
    public void TriggersReportEnterStayAndExitInOrderWithoutBlocking()
    {
        var test = new PhysicsTestWorld();
        var zone = test.Static(new Vector2(0, 0), Collider2D.Box(new Vector2(200, 50)) with { IsTrigger = true });
        var ball = test.Dynamic(new Vector2(0, -100), Collider2D.Circle(5), new Rigidbody2D { Velocity = new Vector2(0, 300) });
        var log = Record(test);

        test.Step(90);

        var kinds = log.Select(e => e.Kind).Distinct().ToArray();
        Assert.Equal(["trigger-enter", "trigger-stay", "trigger-exit"], kinds);
        Assert.Equal(1, log.Count(e => e.Kind == "trigger-enter"));
        Assert.Equal(1, log.Count(e => e.Kind == "trigger-exit"));
        Assert.All(log, e => Assert.True(e.Contact.IsTrigger && e.Contact.Involves(zone) && e.Contact.Involves(ball)));
        Assert.True(test.Position(ball).Y > 100);
    }

    [Fact]
    public void CharactersEnterTriggers()
    {
        var test = new PhysicsTestWorld();
        test.Static(new Vector2(100, 0), Collider2D.Box(new Vector2(20, 20)) with { IsTrigger = true });
        var hero = test.World.Create(new Runtime.Components.Transform(Vector2.Zero), Collider2D.Box(new Vector2(20, 40)), new CharacterController2D());
        var log = Record(test);

        for (var i = 0; i < 30; i++)
        {
            test.Physics.MoveCharacter(hero, new Vector2(5, 0));
            test.Step();
        }

        Assert.Contains(log, e => e.Kind == "trigger-enter" && e.Contact.Involves(hero));
    }

    [Fact]
    public void ListenersSeeContactsFromEachEntity()
    {
        var test = new PhysicsTestWorld();
        var ground = test.Ground();
        var box = test.Dynamic(new Vector2(0, -40), Collider2D.Box(new Vector2(32, 32)));
        var all = new Listener();
        var own = new Listener();
        using var allSubscription = test.Physics.AddCollisionListener(all);
        using var ownSubscription = test.Physics.AddCollisionListener(box, own);

        test.Step(60);

        Assert.Equal(2, all.Entered.Count);
        Assert.Contains(all.Entered, c => c.Self == box && c.Other == ground);
        Assert.Contains(all.Entered, c => c.Self == ground && c.Other == box);
        var mine = Assert.Single(own.Entered);
        Assert.Equal(box, mine.Self);
        Assert.True(mine.Normal.Y < -0.99f, "The normal points from the ground up to the box.");
    }

    [Fact]
    public void DisposedListenersStopReceiving()
    {
        var test = new PhysicsTestWorld();
        test.Ground();
        var box = test.Dynamic(new Vector2(0, -40), Collider2D.Box(new Vector2(32, 32)));
        var listener = new Listener();
        test.Physics.AddCollisionListener(box, listener).Dispose();

        test.Step(60);

        Assert.Empty(listener.Entered);
    }

    [Fact]
    public void CurrentContactsCanBeListedPerEntity()
    {
        var test = new PhysicsTestWorld();
        var ground = test.Ground();
        var box = test.Dynamic(new Vector2(0, -16), Collider2D.Box(new Vector2(32, 32)));
        test.Step(10);

        Span<ContactInfo> contacts = stackalloc ContactInfo[4];
        var count = test.Physics.GetContacts(box, contacts);

        Assert.Equal(1, count);
        Assert.Equal(ground, contacts[0].Other);
        Assert.True(contacts[0].Normal.Y < -0.99f);
    }

    [Fact]
    public void SteppingWithoutSubscribersDoesNotAllocate()
    {
        var test = new PhysicsTestWorld();
        test.Ground();
        for (var i = 0; i < 50; i++)
            test.Dynamic(new Vector2(i * 40 - 1000, -16 - i % 5 * 40), Collider2D.Box(new Vector2(32, 32)), new Rigidbody2D { CanSleep = false });
        test.Step(120);

        // Steady-state allocations show in every round; a one-off, such as the runtime's own work, in only one.
        var fewest = long.MaxValue;
        for (var round = 0; round < 5 && fewest > 0; round++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            test.Step(60);
            fewest = Math.Min(fewest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.Equal(0, fewest);
    }

    private static List<(string Kind, PhysicsContact Contact)> Record(PhysicsTestWorld test)
    {
        var log = new List<(string, PhysicsContact)>();
        test.Events.Subscribe((ref CollisionEntered e) => log.Add(("enter", e.Contact)));
        test.Events.Subscribe((ref CollisionStayed e) => log.Add(("stay", e.Contact)));
        test.Events.Subscribe((ref CollisionExited e) => log.Add(("exit", e.Contact)));
        test.Events.Subscribe((ref PhysicsTriggerEntered e) => log.Add(("trigger-enter", e.Contact)));
        test.Events.Subscribe((ref PhysicsTriggerStayed e) => log.Add(("trigger-stay", e.Contact)));
        test.Events.Subscribe((ref PhysicsTriggerExited e) => log.Add(("trigger-exit", e.Contact)));
        return log;
    }

    private sealed class Listener : ICollisionListener
    {
        public List<ContactInfo> Entered { get; } = [];

        public void OnCollisionEntered(in ContactInfo contact) => Entered.Add(contact);
    }
}
