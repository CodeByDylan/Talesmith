using System.Numerics;
using Talesmith.Authoring;
using Talesmith.Grids;

namespace Talesmith.VFX;

/// <summary>The region particles are emitted from.</summary>
public enum ParticleShapeKind
{
    /// <summary>The emitter's position.</summary>
    Point,

    /// <summary>A segment along the emitter's X axis.</summary>
    Line,

    Rectangle,

    /// <summary>A disc, ring or arc.</summary>
    Circle,

    /// <summary>A base segment emitting in a fan around the direction.</summary>
    Cone,

    /// <summary>One cell of a hex or square grid.</summary>
    Tile,

    /// <summary>Any simple polygon, such as an area of a level.</summary>
    Polygon
}

/// <summary>Whether particles start anywhere inside a shape or only on its outline.</summary>
public enum ParticleEmitFrom
{
    Volume,
    Edge
}

/// <summary>Which way particles start moving.</summary>
public enum ParticleDirectionMode
{
    /// <summary>Away from the shape: outward from circles, tiles and polygons, along the normal of lines, within the cone.</summary>
    Shape,

    /// <summary>Along <see cref="ShapeModule.Angle"/>.</summary>
    Fixed,

    /// <summary>Any direction.</summary>
    Random
}

/// <summary>Where particles are emitted and which way they start moving, relative to the emitter's transform.</summary>
public sealed class ShapeModule
{
    public bool Enabled = true;

    [Tooltip("The region particles are emitted from.")]
    public ParticleShapeKind Kind = ParticleShapeKind.Point;

    [Label("Emit from")]
    [Tooltip("Anywhere inside the shape, or only on its outline.")]
    public ParticleEmitFrom EmitFrom = ParticleEmitFrom.Volume;

    [Tooltip("The radius of circles, and half the width of a cone's base.")]
    [Range(0, 10000)]
    public float Radius = 16;

    [Label("Inner radius")]
    [Tooltip("Turns a circle into a ring; particles start between this and the radius.")]
    [Range(0, 10000)]
    public float InnerRadius;

    [Tooltip("The part of the circle that emits, starting at the shape's rotation.")]
    [Angle]
    [Range(0, 6.2831853)]
    public float Arc = MathF.Tau;

    [Tooltip("The size of a rectangle; a line uses the width as its length.")]
    public Vector2 Size = new(32, 32);

    [Label("Cone angle")]
    [Tooltip("The full opening angle of a cone.")]
    [Angle]
    [Range(0, 3.1415927)]
    public float ConeAngle = 0.5f;

    [Label("Tile kind")]
    [Tooltip("The grid whose cell shape a tile emitter uses.")]
    public GridKind Tile = GridKind.HexPointyTop;

    [Label("Tile size")]
    [Tooltip("The bounding box of one grid cell in world units.")]
    public Vector2 CellSize = new(64, 64);

    [Tooltip("The outline of a polygon, relative to the emitter.")]
    public List<Vector2> Points = [];

    [Tooltip("Moves the shape relative to the emitter.")]
    public Vector2 Offset;

    [Tooltip("Turns the shape, clockwise.")]
    [Angle]
    public float Rotation;

    [Tooltip("Which way particles start moving.")]
    public ParticleDirectionMode Direction = ParticleDirectionMode.Fixed;

    [Tooltip("The direction for fixed emission, point and line shapes, and the axis of cones; -90° points up.")]
    [Angle]
    public float Angle = -MathF.PI / 2;

    [Tooltip("Turns each particle's direction by a random amount within this angle.")]
    [Angle]
    [Range(0, 6.2831853)]
    public float Spread;

    [Label("Randomize position")]
    [Tooltip("Moves each particle's start by up to this distance in a random direction.")]
    [Range(0, 10000)]
    public float RandomizePosition;
}
