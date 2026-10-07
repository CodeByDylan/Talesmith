namespace Talesmith.Physics.Simulation;

/// <summary>Groups touching dynamic bodies into islands; an island sleeps once all its bodies have rested long enough, and wakes as a whole.</summary>
internal sealed class SleepSolver(PhysicsState state)
{
    private int[] _parent = new int[64];
    private float[] _minSleep = new float[64];
    private byte[] _awake = new byte[64];

    public void Update(float dt)
    {
        var settings = state.Settings;
        var bodies = state.Bodies;
        var count = state.BodyHighWater;
        if (_parent.Length < count)
        {
            var size = Math.Max(count, _parent.Length * 2);
            Array.Resize(ref _parent, size);
            Array.Resize(ref _minSleep, size);
            Array.Resize(ref _awake, size);
        }

        var linear = settings.SleepLinearVelocity * settings.SleepLinearVelocity;
        var angular = settings.SleepAngularVelocity * settings.SleepAngularVelocity;
        for (var i = 0; i < count; i++)
        {
            _parent[i] = i;
            _minSleep[i] = float.MaxValue;
            _awake[i] = 0;
            ref var body = ref bodies[i];
            if (!body.IsAlive || body.Kind != BodyKind.Dynamic || !body.IsAwake)
                continue;
            if (!settings.AllowSleeping || !body.Has(BodyFlags.CanSleep) || body.Velocity.LengthSquared() > linear
                || body.AngularVelocity * body.AngularVelocity > angular)
                body.SleepTime = 0;
            else
                body.SleepTime += dt;
        }

        for (var c = 0; c < state.ActiveContactCount; c++)
        {
            ref var contact = ref state.Contacts[state.ActiveContacts[c]];
            if (!contact.IsSolid || bodies[contact.BodyA].Kind != BodyKind.Dynamic || bodies[contact.BodyB].Kind != BodyKind.Dynamic)
                continue;
            Union(contact.BodyA, contact.BodyB);
        }

        for (var i = 0; i < count; i++)
        {
            ref var body = ref bodies[i];
            if (!body.IsAlive || body.Kind != BodyKind.Dynamic)
                continue;
            var root = Find(i);
            if (body.IsAwake)
            {
                _awake[root] = 1;
                _minSleep[root] = MathF.Min(_minSleep[root], body.SleepTime);
            }
        }

        for (var i = 0; i < count; i++)
        {
            ref var body = ref bodies[i];
            if (!body.IsAlive || body.Kind != BodyKind.Dynamic)
                continue;
            var root = Find(i);
            if (_awake[root] == 0)
                continue;
            if (_minSleep[root] >= settings.TimeToSleep)
            {
                if (body.IsAwake)
                    state.SleepBody(i);
            }
            else if (!body.IsAwake)
            {
                state.WakeBody(i);
            }
        }
    }

    private int Find(int i)
    {
        while (_parent[i] != i)
        {
            _parent[i] = _parent[_parent[i]];
            i = _parent[i];
        }

        return i;
    }

    private void Union(int a, int b)
    {
        var rootA = Find(a);
        var rootB = Find(b);
        if (rootA == rootB)
            return;
        if (rootA < rootB)
            _parent[rootB] = rootA;
        else
            _parent[rootA] = rootB;
    }
}
