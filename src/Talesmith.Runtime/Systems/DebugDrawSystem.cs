using System.Numerics;
using Talesmith.Assets.Maps;
using Talesmith.Ecs;
using Talesmith.Grids;
using Talesmith.Input;
using Talesmith.Mathematics;
using Talesmith.Rendering;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Diagnostics;
using Talesmith.Runtime.Rendering;
using Talesmith.Systems;

namespace Talesmith.Runtime.Systems;

/// <summary>Draws the developer overlays enabled in <see cref="DebugOptions"/>.</summary>
[UpdateIn(SystemPhase.PreRender)]
[SystemOrder(1000)]
public sealed class DebugDrawSystem(DebugOptions options, RenderContext render, IInputService input) : ISystem
{
    private static readonly QueryDescription ActiveMaps = QueryDescription.With<TileMapComponent>().And<Transform>().Without<Inactive>();
    private static readonly QueryDescription ActiveObjects = QueryDescription.With<Transform>().And<MapObjectComponent>().Without<Inactive>();
    private static readonly Color GridColor = new(255, 255, 255, 90);
    private static readonly Color HoverColor = new(255, 214, 10);
    private static readonly Color ChunkColor = new(99, 102, 241, 200);
    private static readonly Color ObjectColor = new(16, 185, 129, 230);

    private readonly List<(Transform Transform, MapObjectComponent Object)> _objects = [];

    public void Update(in SystemContext context)
    {
        if (!options.Any)
            return;

        var frame = render.Frame;
        var thickness = 1.5f / Math.Max(0.01f, frame.Camera.Zoom);
        foreach (var archetype in context.World.Query(ActiveMaps))
        {
            var components = archetype.GetSpan<TileMapComponent>();
            var transforms = archetype.GetSpan<Transform>();
            for (var i = 0; i < components.Length; i++)
            {
                var map = components[i].Map;
                var origin = transforms[i].Position;
                if (options.ShowChunks)
                    DrawChunks(frame, map, origin, thickness);
                if (options.ShowGrid)
                    DrawGrid(frame, map.Layout, origin, thickness);
            }
        }

        if (options.ShowObjects)
            DrawObjects(frame, context.World, thickness);
    }

    private static void DrawChunks(RenderFrame frame, TileMap map, Vector2 mapOrigin, float thickness)
    {
        var (min, max) = ChunkCoord.Covering(map.Layout.CoveringBounds(frame.VisibleBounds.Offset(-mapOrigin)), map.ChunkShift);
        foreach (var layer in map.TileLayers)
        {
            for (var y = min.Y; y <= max.Y; y++)
            {
                for (var x = min.X; x <= max.X; x++)
                {
                    if (!layer.Chunks.ContainsKey(new ChunkCoord(x, y)))
                        continue;
                    var origin = new ChunkCoord(x, y).Origin(map.ChunkShift);
                    var last = new GridCoord(origin.X + layer.ChunkSize - 1, origin.Y + layer.ChunkSize - 1);
                    var bounds = map.Layout.CellBounds(origin).Union(map.Layout.CellBounds(last))
                        .Union(map.Layout.CellBounds(new GridCoord(last.X, origin.Y))).Union(map.Layout.CellBounds(new GridCoord(origin.X, last.Y)));
                    frame.DrawRectOutline(bounds.Offset(mapOrigin), thickness * 2, ChunkColor, RenderLayers.Debug);
                }
            }
        }
    }

    private void DrawGrid(RenderFrame frame, IGridLayout layout, Vector2 mapOrigin, float thickness)
    {
        var hovered = layout.WorldToCell(render.ScreenToWorld(input.MousePosition) - mapOrigin);
        Span<Vector2> corners = stackalloc Vector2[layout.CornerCount];
        for (var dy = -3; dy <= 3; dy++)
        {
            for (var dx = -3; dx <= 3; dx++)
            {
                var cell = new GridCoord(hovered.X + dx, hovered.Y + dy);
                if (layout.Distance(cell, hovered) > 3)
                    continue;
                var center = mapOrigin + layout.CellToWorld(cell);
                for (var i = 0; i < corners.Length; i++)
                    corners[i] = center + layout.CornerOffset(i);
                frame.DrawPolygonOutline(corners, cell == hovered ? thickness * 2.5f : thickness, cell == hovered ? HoverColor : GridColor, RenderLayers.Debug);
            }
        }
    }

    private void DrawObjects(RenderFrame frame, World world, float thickness)
    {
        _objects.Clear();
        foreach (var archetype in world.Query(ActiveObjects))
        {
            var transforms = archetype.GetSpan<Transform>();
            var objects = archetype.GetSpan<MapObjectComponent>();
            for (var i = 0; i < transforms.Length; i++)
                _objects.Add((transforms[i], objects[i]));
        }

        Span<Vector2> points = stackalloc Vector2[64];
        foreach (var (transform, component) in _objects)
        {
            var mapObject = component.Object;
            var color = component.Layer.Color ?? ObjectColor;
            if (mapObject.Shape == MapObjectShape.Polygon && mapObject.Polygon.Count is >= 3 and <= 64)
            {
                for (var i = 0; i < mapObject.Polygon.Count; i++)
                    points[i] = transform.Position + mapObject.Polygon[i];
                frame.DrawPolygonOutline(points[..mapObject.Polygon.Count], thickness * 2, color, RenderLayers.Debug);
            }
            else
            {
                var size = 8 * thickness;
                frame.FillRect(Rect2.FromCenter(transform.Position, new Vector2(size)), color, RenderLayers.Debug);
            }
        }
    }
}
