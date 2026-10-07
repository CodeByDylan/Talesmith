using System.Numerics;
using Talesmith.Mathematics;
using Talesmith.Physics.Simulation;
using Talesmith.Rendering;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Diagnostics;
using Talesmith.Runtime.Rendering;
using Talesmith.Systems;

namespace Talesmith.Physics.Systems;

/// <summary>Physics overlays drawn over the game, in addition to the runtime's <see cref="DebugOptions"/>.</summary>
/// <remarks>The runtime's object view (<see cref="DebugOptions.ShowObjects"/>) also shows colliders and contacts, and its chunk view shows bounds.</remarks>
public sealed class PhysicsDebugOptions
{
    /// <summary>Outlines colliders, colored by body type and state, and the collision of tile maps.</summary>
    public bool ShowColliders { get; set; }

    /// <summary>Marks contact points with their normals.</summary>
    public bool ShowContacts { get; set; }

    /// <summary>Outlines the broadphase bounds of every shape.</summary>
    public bool ShowBounds { get; set; }
}

/// <summary>Draws colliders, contacts and bounds when physics debug views are on.</summary>
/// <remarks>Colliders are drawn from their components, so they show in every execution mode; contacts and bounds need a running simulation.</remarks>
[UpdateIn(SystemPhase.PreRender)]
[ExecuteIn(ExecutionModes.All)]
[SystemOrder(1001)]
public sealed class PhysicsDebugDrawSystem(DebugOptions debug, PhysicsDebugOptions options, RenderContext render, PhysicsWorld physics) : ISystem
{
    private static readonly Color StaticColor = new(148, 163, 184, 220);
    private static readonly Color DynamicColor = new(74, 222, 128, 230);
    private static readonly Color SleepingColor = new(34, 120, 70, 200);
    private static readonly Color KinematicColor = new(167, 139, 250, 230);
    private static readonly Color TriggerColor = new(250, 204, 21, 200);
    private static readonly Color TileColor = new(56, 189, 248, 200);
    private static readonly Color ContactColor = new(248, 113, 113, 255);
    private static readonly Color BoundsColor = new(244, 114, 182, 120);

    private readonly Vector2[] _points = new Vector2[256];

    public void Update(in SystemContext context)
    {
        var colliders = options.ShowColliders || debug.ShowObjects;
        var contacts = options.ShowContacts || debug.ShowObjects;
        var bounds = options.ShowBounds || (debug.ShowObjects && debug.ShowChunks);
        if (!colliders && !contacts && !bounds)
            return;

        var frame = render.Frame;
        var thickness = 1.5f / Math.Max(0.01f, frame.Camera.Zoom);
        if (colliders)
        {
            DrawColliders(frame, context.World, thickness);
            DrawTiles(frame, thickness);
        }

        if (contacts)
            DrawContacts(frame, thickness);
        if (bounds)
            DrawBounds(frame, thickness);
    }

    private void DrawColliders(RenderFrame frame, Ecs.World world, float thickness)
    {
        var state = physics.State;
        foreach (var archetype in world.Query<Collider2D, Transform>())
        {
            var entities = archetype.Entities;
            var colliders = archetype.GetSpan<Collider2D>();
            var transforms = archetype.GetSpan<Transform>();
            for (var i = 0; i < entities.Length; i++)
            {
                ref var collider = ref colliders[i];
                var count = ColliderGeometry.GetOutline(collider, transforms[i], _points);
                if (count < 2)
                    continue;
                var outline = _points.AsSpan(0, count);
                if (!frame.IsVisible(Rect2.Bounding(outline)))
                    continue;
                var color = collider.IsTrigger ? TriggerColor : ColorOf(state, state.BodyOf(entities[i]));
                frame.DrawPolygonOutline(outline, collider.IsTrigger ? thickness : thickness * 1.5f, color, RenderLayers.Debug);
            }
        }
    }

    private void DrawTiles(RenderFrame frame, float thickness)
    {
        var state = physics.State;
        for (var f = 0; f < state.FixtureHighWater; f++)
        {
            ref var fixture = ref state.Fixtures[f];
            if (!fixture.Alive || !state.Bodies[fixture.Body].Has(BodyFlags.TileChunk) || !frame.IsVisible(fixture.Aabb.ToRect()))
                continue;
            var count = ColliderGeometry.GetOutline(fixture.World, _points, ColliderGeometry.DefaultCircleSegments);
            frame.DrawPolygonOutline(_points.AsSpan(0, count), thickness, fixture.OneWay ? TriggerColor : TileColor, RenderLayers.Debug);
        }
    }

    private void DrawContacts(RenderFrame frame, float thickness)
    {
        var state = physics.State;
        var size = 4 * thickness;
        var normalLength = 12 * thickness;
        for (var c = 0; c < state.ActiveContactCount; c++)
        {
            ref var contact = ref state.Contacts[state.ActiveContacts[c]];
            if (!contact.IsTouching || contact.IsDisabled)
                continue;
            ref var manifold = ref contact.Manifold;
            for (var j = 0; j < manifold.PointCount; j++)
            {
                var point = manifold.Points[j].Point;
                if (!frame.IsVisible(Rect2.FromCenter(point, new Vector2(normalLength * 2))))
                    continue;
                frame.FillRect(Rect2.FromCenter(point, new Vector2(size)), ContactColor, RenderLayers.Debug);
                frame.DrawLine(point, point + manifold.Normal * normalLength, thickness, ContactColor, RenderLayers.Debug);
            }
        }
    }

    private void DrawBounds(RenderFrame frame, float thickness)
    {
        var state = physics.State;
        for (var f = 0; f < state.FixtureHighWater; f++)
        {
            ref var fixture = ref state.Fixtures[f];
            if (!fixture.Alive)
                continue;
            var rect = state.Broadphase.GetFatAabb(fixture.Proxy).ToRect();
            if (frame.IsVisible(rect))
                frame.DrawRectOutline(rect, thickness, BoundsColor, RenderLayers.Debug);
        }
    }

    private static Color ColorOf(PhysicsState state, int body)
    {
        if (body < 0)
            return StaticColor;
        ref var b = ref state.Bodies[body];
        return b.Kind switch
        {
            BodyKind.Dynamic => b.IsAwake ? DynamicColor : SleepingColor,
            BodyKind.Kinematic => KinematicColor,
            _ => StaticColor
        };
    }
}
