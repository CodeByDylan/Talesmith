using System.Numerics;

namespace Talesmith.Physics;

/// <summary>The broadphase structure that finds which shapes may touch.</summary>
public enum BroadphaseKind
{
    /// <summary>A balanced bounding volume tree; good for every mix of shape sizes, long rays and huge static shapes.</summary>
    DynamicTree,

    /// <summary>A uniform grid of <see cref="PhysicsSettings.SpatialHashCellSize"/>; good for many similar small bodies.</summary>
    SpatialHash
}

/// <summary>Tuning of a physics world. Each scene's world starts from a copy of the registered settings.</summary>
/// <remarks>
/// Distances are in world units with Y pointing down. Tolerances such as <see cref="LinearSlop"/> scale with
/// <see cref="LengthUnitsPerMeter"/>, so worlds measured in pixels behave like worlds measured in meters.
/// </remarks>
public sealed class PhysicsSettings
{
    private int[] _layerCollisions = new int[PhysicsLayers.Count];

    public PhysicsSettings()
    {
        Array.Fill(_layerCollisions, PhysicsLayers.All);
    }

    /// <summary>Acceleration applied to dynamic bodies, in world units per second squared; positive Y falls down.</summary>
    public Vector2 Gravity { get; set; } = new(0, 980);

    /// <summary>World units in one meter, which scales the solver's tolerances; 100 suits pixel-art worlds.</summary>
    public float LengthUnitsPerMeter
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = 100;

    /// <summary>Solver substeps per fixed step; more gives stiffer stacks and handles larger mass ratios, at proportional cost.</summary>
    public int Substeps { get; set; } = 4;

    /// <summary>Passes over contacts per substep that resolve velocities and push overlapping shapes apart.</summary>
    public int VelocityIterations { get; set; } = 1;

    /// <summary>Passes over contacts per substep that resolve velocities again without pushing, so pushing apart adds no bounce.</summary>
    public int RelaxIterations { get; set; } = 1;

    /// <summary>The stiffness of contacts, in hertz, when pushing overlapping shapes apart; capped at a quarter of the substep rate.</summary>
    public float ContactHertz { get; set; } = 30;

    /// <summary>How strongly the push apart is damped; values above 1 avoid overshooting.</summary>
    public float ContactDampingRatio { get; set; } = 10;

    /// <summary>The fastest overlapping shapes are pushed apart, in world units per second.</summary>
    public float ContactPushMaxVelocity { get; set; } = 300;

    /// <summary>Reuses last step's contact impulses as a starting point, which steadies stacks.</summary>
    public bool WarmStarting { get; set; } = true;

    /// <summary>Lets resting bodies stop simulating.</summary>
    public bool AllowSleeping { get; set; } = true;

    /// <summary>Bodies slower than this, in world units per second, may fall asleep.</summary>
    public float SleepLinearVelocity { get; set; } = 1;

    /// <summary>Bodies turning slower than this, in radians per second, may fall asleep.</summary>
    public float SleepAngularVelocity { get; set; } = 2 * MathF.PI / 180;

    /// <summary>Seconds a body must stay slow before it falls asleep.</summary>
    public float TimeToSleep { get; set; } = 0.5f;

    /// <summary>Impacts slower than this, in world units per second, do not bounce, so resting bodies settle.</summary>
    public float RestitutionThreshold { get; set; } = 100;

    public BroadphaseKind Broadphase { get; set; } = BroadphaseKind.DynamicTree;

    /// <summary>The cell size of <see cref="BroadphaseKind.SpatialHash"/>; about twice the size of a typical body works well.</summary>
    public float SpatialHashCellSize { get; set; } = 128;

    /// <summary>How much shapes may overlap at rest, in world units; allowing a little keeps contacts stable.</summary>
    public float LinearSlop => 0.005f * LengthUnitsPerMeter;

    internal float SpeculativeDistance => 4 * LinearSlop;

    internal float AabbMargin => 0.02f * LengthUnitsPerMeter;

    internal float MaxTranslationPerStep => 4 * LengthUnitsPerMeter;

    /// <summary>Whether shapes on layers <paramref name="a"/> and <paramref name="b"/> collide; every pair does by default.</summary>
    public bool ShouldLayersCollide(int a, int b) => (_layerCollisions[a & 31] & (1 << (b & 31))) != 0;

    /// <summary>Sets whether shapes on two layers collide, in both directions.</summary>
    public void SetLayerCollision(int a, int b, bool collide)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(a);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(a, PhysicsLayers.Count);
        ArgumentOutOfRangeException.ThrowIfNegative(b);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(b, PhysicsLayers.Count);
        if (collide)
        {
            _layerCollisions[a] |= 1 << b;
            _layerCollisions[b] |= 1 << a;
        }
        else
        {
            _layerCollisions[a] &= ~(1 << b);
            _layerCollisions[b] &= ~(1 << a);
        }
    }

    /// <summary>The mask of layers that collide with <paramref name="layer"/>.</summary>
    public int LayerCollisionMask(int layer) => _layerCollisions[layer & 31];

    public PhysicsSettings Clone()
    {
        var copy = (PhysicsSettings)MemberwiseClone();
        copy._layerCollisions = (int[])_layerCollisions.Clone();
        return copy;
    }
}
