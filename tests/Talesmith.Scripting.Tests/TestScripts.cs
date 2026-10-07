using System.Numerics;
using Talesmith.Authoring;
using Talesmith.Ecs;
using Talesmith.Physics;
using Talesmith.Runtime.Scenes;

namespace Talesmith.Scripting.Tests;

public sealed class Recorder : Script
{
    [Transient]
    public List<string> Calls = [];

    public int Count(string call) => Calls.Count(c => c == call);

    protected override void OnCreate() => Calls.Add(nameof(OnCreate));

    protected override void OnStart() => Calls.Add(nameof(OnStart));

    protected override void OnEnable() => Calls.Add(nameof(OnEnable));

    protected override void OnDisable() => Calls.Add(nameof(OnDisable));

    protected override void FixedUpdate() => Calls.Add(nameof(FixedUpdate));

    protected override void Update() => Calls.Add(nameof(Update));

    protected override void LateUpdate() => Calls.Add(nameof(LateUpdate));

    protected override void OnDestroy() => Calls.Add(nameof(OnDestroy));

    protected override void OnSceneLoaded(Scene scene) => Calls.Add(nameof(OnSceneLoaded));

    protected override void OnSceneUnloaded(Scene scene) => Calls.Add(nameof(OnSceneUnloaded));
}

public sealed class Mover : Script
{
    [Range(0, 1000)]
    public float Speed = 10;

    public Vector2 Direction = Vector2.UnitX;

    protected override void Update() => Position += Direction * Speed * Time.DeltaTime;
}

public sealed class Thrower : Script
{
    [Transient]
    public int Calls;

    protected override void Update()
    {
        Calls++;
        throw new InvalidOperationException("Thrown on purpose.");
    }
}

public sealed class ContactRecorder : Script
{
    [Transient]
    public List<string> Contacts = [];

    [Transient]
    public Entity LastOther;

    protected override void OnCollisionEnter(in ContactInfo contact)
    {
        Contacts.Add("collision enter");
        LastOther = contact.Other;
    }

    protected override void OnCollisionExit(in ContactInfo contact) => Contacts.Add("collision exit");

    protected override void OnTriggerEnter(in ContactInfo contact)
    {
        Contacts.Add("trigger enter");
        LastOther = contact.Other;
    }
}

public readonly record struct CoinCollected(int Value);

public sealed class Coin : Script
{
    public int Value = 1;

    protected override void OnTriggerEnter(in ContactInfo contact)
    {
        Events.Publish(new CoinCollected(Value));
        Destroy();
    }
}

public sealed class Wallet : Script
{
    [Transient]
    public int Total;

    protected override void OnCreate() => Events.Subscribe((ref CoinCollected e) => Total += e.Value);
}

public sealed class Waiter : Script
{
    [Transient]
    public int Stage;

    protected override void OnStart() => Run(async () =>
    {
        Stage = 1;
        await Wait(0.5);
        Stage = 2;
        await NextFrame();
        Stage = 3;
    });
}

public sealed class Spawner : Script
{
    [Transient]
    public Mover? Spawned;

    protected override void OnStart() => Spawned = AddScript<Mover>();
}

public enum Facing
{
    Left,
    Right
}

public sealed class Fields : Script
{
    public int Count;

    public string? Label;

    public Entity Target;

    public List<int> Numbers = [];

    public Facing Facing = Facing.Right;

    public Mover? Partner;

    [SerializeField]
    private float _secret;

    [field: SerializeField]
    public int Level { get; private set; }

    [Transient]
    public int Cache;

    public float Secret => _secret;

    public void SetSecret(float value, int level)
    {
        _secret = value;
        Level = level;
    }
}

public sealed class Busy : Script
{
    public float Speed = 1;

    protected override void Update() => Transform.Rotation += Speed * Time.DeltaTime;
}
