using System.Numerics;
using Talesmith.Events;
using Talesmith.Physics;
using Talesmith.Runtime.Components;

namespace Talesmith.Scripting.Tests;

public sealed class CollisionTests
{
    [Fact]
    public async Task ScriptsReceiveCollisionsOfTheirEntityFromThePhysicsStep()
    {
        await using var test = await ScriptTestGame.StartAsync();
        var floor = test.World.Create(new Transform(new Vector2(0, 100)), Collider2D.Box(new Vector2(400, 20)));
        var recorder = new ContactRecorder();
        var ball = test.World.Create(new Transform(Vector2.Zero), Collider2D.Circle(10), new Rigidbody2D(), new ScriptComponent(recorder));

        test.Tick(90);

        Assert.Equal("collision enter", recorder.Contacts[0]);
        Assert.Equal(floor, recorder.LastOther);

        test.World.Destroy(floor);
        test.Tick(2);
        Assert.Contains("collision exit", recorder.Contacts);
        Assert.True(test.World.IsAlive(ball));
    }

    [Fact]
    public async Task TriggerScriptsCanPublishEventsAndDestroyTheirEntity()
    {
        await using var test = await ScriptTestGame.StartAsync();
        var wallet = new Wallet();
        test.World.Create(new ScriptComponent(wallet));
        var coin = test.World.Create(new Transform(new Vector2(0, 60)), Collider2D.Box(new Vector2(40, 40)) with { IsTrigger = true },
            new ScriptComponent(new Coin { Value = 5 }));
        var recorder = new ContactRecorder();
        test.World.Create(new Transform(Vector2.Zero), Collider2D.Circle(10), new Rigidbody2D(), new ScriptComponent(recorder));

        test.Tick(60);

        Assert.False(test.World.IsAlive(coin));
        Assert.Equal(5, wallet.Total);
        Assert.Contains("trigger enter", recorder.Contacts);
    }

    [Fact]
    public async Task SubscriptionsEndWhenTheScriptIsDestroyed()
    {
        await using var test = await ScriptTestGame.StartAsync();
        var wallet = new Wallet();
        var entity = test.World.Create(new ScriptComponent(wallet));
        test.Tick();
        var events = test.Service<IEventBus>();
        events.Publish(new CoinCollected(2));
        Assert.Equal(2, wallet.Total);

        test.World.Destroy(entity);
        events.Publish(new CoinCollected(3));

        Assert.Equal(2, wallet.Total);
        Assert.False(events.HasSubscribers<CoinCollected>());
    }
}
