using System.Numerics;
using Talesmith.Ecs;
using Talesmith.Physics.Broadphase;
using Talesmith.Physics.Collision;
using Talesmith.Physics.Geometry;
using Talesmith.Physics.Tiles;

namespace Talesmith.Physics.Simulation;

/// <summary>Ray casts, shape casts and overlap tests against the world's fixtures.</summary>
internal sealed class QueryEngine(PhysicsState state, TileColliderCache tiles)
{
    private readonly List<Shape> _shapes = [];
    private RaycastHit[] _hits = new RaycastHit[16];

    public bool RayCast(Vector2 origin, Vector2 direction, float maxDistance, in QueryFilter filter, out RaycastHit hit)
    {
        hit = default;
        var translation = Vec.Normalize(direction) * maxDistance;
        if (translation == Vector2.Zero)
            return false;
        tiles.EnsureRegion(new Aabb(Vector2.Min(origin, origin + translation), Vector2.Max(origin, origin + translation)));
        var callback = new ClosestRay(state, filter, origin, translation);
        state.Broadphase.RayCast(origin, translation, 1, ref callback);
        if (!callback.Found)
            return false;
        hit = callback.Hit;
        return true;
    }

    public int RayCastAll(Vector2 origin, Vector2 direction, float maxDistance, in QueryFilter filter, Span<RaycastHit> results)
    {
        var translation = Vec.Normalize(direction) * maxDistance;
        if (translation == Vector2.Zero || results.IsEmpty)
            return 0;
        tiles.EnsureRegion(new Aabb(Vector2.Min(origin, origin + translation), Vector2.Max(origin, origin + translation)));
        var callback = new AllRays(this, filter, origin, translation);
        state.Broadphase.RayCast(origin, translation, 1, ref callback);

        var count = callback.Count;
        var hits = _hits.AsSpan(0, count);
        hits.Sort(static (a, b) => a.Fraction.CompareTo(b.Fraction));
        count = Math.Min(count, results.Length);
        hits[..count].CopyTo(results);
        return count;
    }

    public bool ShapeCast(in QueryShape shape, Vector2 position, float rotation, Vector2 direction, float maxDistance, in QueryFilter filter, out RaycastHit hit)
    {
        hit = default;
        var translation = Vec.Normalize(direction) * maxDistance;
        if (!BuildShapes(shape, position, rotation))
            return false;
        var best = 1f;
        var found = false;
        foreach (var piece in _shapes)
        {
            var bounds = piece.ComputeAabb().Sweep(translation);
            tiles.EnsureRegion(bounds);
            var callback = new CastQuery(state, filter, piece, translation, best);
            state.Broadphase.Query(bounds, ref callback);
            if (!callback.Found)
                continue;
            found = true;
            best = callback.Best;
            hit = callback.Hit with { Distance = callback.Best * maxDistance };
        }

        return found;
    }

    public int OverlapPoint(Vector2 point, in QueryFilter filter, Span<Entity> results)
    {
        var bounds = new Aabb(point, point);
        tiles.EnsureRegion(bounds);
        var callback = new PointQuery(state, filter, point);
        state.Broadphase.Query(bounds, ref callback);
        return Collect(callback.First, results);
    }

    public int OverlapShape(in QueryShape shape, Vector2 position, float rotation, in QueryFilter filter, Span<Entity> results)
    {
        if (!BuildShapes(shape, position, rotation) || results.IsEmpty)
            return 0;
        var count = 0;
        foreach (var piece in _shapes)
        {
            var bounds = piece.ComputeAabb();
            tiles.EnsureRegion(bounds);
            var callback = new ShapeQuery(state, filter, piece);
            state.Broadphase.Query(bounds, ref callback);
            count = Merge(callback.First, results, count);
        }

        return count;
    }

    public static bool Passes(PhysicsState state, in Fixture fixture, in QueryFilter filter) =>
        (!fixture.IsTrigger || filter.IncludeTriggers) && PhysicsLayers.Contains(filter.LayerMask, fixture.Layer)
        && (filter.Ignore.IsNull || state.Bodies[fixture.Body].Entity != filter.Ignore);

    private bool BuildShapes(in QueryShape shape, Vector2 position, float rotation)
    {
        _shapes.Clear();
        try
        {
            ColliderShapes.Build(shape, _shapes);
        }
        catch (ArgumentException)
        {
            return false;
        }

        var xf = new Xf(position, rotation);
        for (var i = 0; i < _shapes.Count; i++)
            _shapes[i] = _shapes[i].Transform(xf);
        return true;
    }

    private void AddHit(in RaycastHit hit, ref int count)
    {
        if (count == _hits.Length)
            Array.Resize(ref _hits, _hits.Length * 2);
        _hits[count++] = hit;
    }

    /// <summary>Entities found by a query, chained through the fixture list so collecting them does not allocate.</summary>
    private int Collect(int first, Span<Entity> results) => Merge(first, results, 0);

    private int Merge(int first, Span<Entity> results, int count)
    {
        for (var f = first; f != PhysicsState.Null; f = state.Fixtures[f].QueryNext)
        {
            var entity = state.Bodies[state.Fixtures[f].Body].Entity;
            if (results[..count].Contains(entity))
                continue;
            if (count == results.Length)
                break;
            results[count++] = entity;
        }

        return count;
    }

    private struct ClosestRay(PhysicsState state, QueryFilter filter, Vector2 origin, Vector2 translation) : IProxyRayCast
    {
        public bool Found;
        public RaycastHit Hit;

        public float Report(int proxy, int userData, float maxFraction)
        {
            ref var fixture = ref state.Fixtures[userData];
            if (!Passes(state, fixture, filter))
                return -1;
            var result = Collision.RayCast.Shape(fixture.World, origin, translation, maxFraction);
            if (!result.Hit)
                return -1;
            Found = true;
            Hit = new RaycastHit(state.Bodies[fixture.Body].Entity, result.Point, result.Normal, result.Fraction * translation.Length(), result.Fraction,
                fixture.IsTrigger);
            return result.Fraction;
        }
    }

    private struct AllRays(QueryEngine engine, QueryFilter filter, Vector2 origin, Vector2 translation) : IProxyRayCast
    {
        public int Count;

        public float Report(int proxy, int userData, float maxFraction)
        {
            ref var fixture = ref engine.State.Fixtures[userData];
            if (!Passes(engine.State, fixture, filter))
                return -1;
            var result = Collision.RayCast.Shape(fixture.World, origin, translation, 1);
            if (!result.Hit)
                return -1;
            var hit = new RaycastHit(engine.State.Bodies[fixture.Body].Entity, result.Point, result.Normal, result.Fraction * translation.Length(),
                result.Fraction, fixture.IsTrigger);
            engine.AddHit(hit, ref Count);
            return -1;
        }
    }

    private struct CastQuery(PhysicsState state, QueryFilter filter, Shape shape, Vector2 translation, float best) : IProxyQuery
    {
        public bool Found;
        public float Best = best;
        public RaycastHit Hit;

        public bool Report(int proxy, int userData)
        {
            ref var fixture = ref state.Fixtures[userData];
            if (!Passes(state, fixture, filter))
                return true;
            var slop = state.Settings.LinearSlop;
            var cast = Distance.Cast(fixture.World, shape, translation, Best, 0, 0.5f * slop);
            if (!cast.Hit || (Found && cast.Fraction >= Best))
                return true;
            Found = true;
            Best = cast.Fraction;
            Hit = new RaycastHit(state.Bodies[fixture.Body].Entity, cast.Point, cast.Normal, 0, cast.Fraction, fixture.IsTrigger);
            return true;
        }
    }

    private struct PointQuery(PhysicsState state, QueryFilter filter, Vector2 point) : IProxyQuery
    {
        public int First = PhysicsState.Null;

        public bool Report(int proxy, int userData)
        {
            ref var fixture = ref state.Fixtures[userData];
            if (Passes(state, fixture, filter) && fixture.World.ContainsPoint(point))
            {
                fixture.QueryNext = First;
                First = userData;
            }

            return true;
        }
    }

    private struct ShapeQuery(PhysicsState state, QueryFilter filter, Shape shape) : IProxyQuery
    {
        public int First = PhysicsState.Null;

        public bool Report(int proxy, int userData)
        {
            ref var fixture = ref state.Fixtures[userData];
            if (!Passes(state, fixture, filter))
                return true;
            Collide.Shapes(fixture.World, shape, 0, state.Settings.LinearSlop, out var manifold);
            if (manifold.PointCount > 0 && manifold.MinSeparation < 0)
            {
                fixture.QueryNext = First;
                First = userData;
            }

            return true;
        }
    }

    private PhysicsState State => state;
}
